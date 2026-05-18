using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Serialization
{
    /// <summary>프리로딩 우선순위</summary>
    public enum PreloadPriority
    {
        /// <summary>낮은 우선순위 (역재생 대비)</summary>
        Low = 0,

        /// <summary>보통 우선순위 (인접 청크)</summary>
        Normal = 1,

        /// <summary>높은 우선순위 (즉시 필요)</summary>
        High = 2
    }

    /// <summary>프리로딩 작업</summary>
    internal struct PreloadTask
    {
        public int ChunkIndex;
        public PreloadPriority Priority;
        public CancellationTokenSource Cts;
    }

    /// <summary>청크 스트리밍 관리자입니다 (LZ4/GZip 지원, 개선된 프리로딩).</summary>
    public class ChunkStreamManager : IDisposable
    {
        #region Fields

        private readonly string filePath;
        private readonly ReplayData replayData;
        private readonly ConcurrentDictionary<int, ReplayChunk> chunkCache; // 스레드 안전
        private readonly int maxCachedChunks;
        private readonly LinkedList<int> lruOrder; // LRU 캐시 순서
        private readonly Dictionary<int, LinkedListNode<int>> lruNodes;
        private readonly object lruLock = new object(); // LRU 연산 동기화

        // 프리로딩 관련 (ConcurrentDictionary로 Race Condition 방지)
        private readonly ConcurrentDictionary<int, UniTask<ReplayChunk>> pendingLoads;
        private readonly SortedSet<PreloadTask> preloadQueue;
        private readonly object preloadLock = new object();
        private readonly SemaphoreSlim chunkLoadSemaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource preloadCts;
        private bool isPreloading;

        // 파일 접근 동기화
        private readonly SemaphoreSlim fileAccessSemaphore = new SemaphoreSlim(1, 1);

        private FileStream fileStream;
        private ReCompressionType compressionType;
        private bool isDisposed;

        // 통계
        private int cacheHits;
        private int cacheMisses;

        #endregion

        #region Properties

        /// <summary>현재 캐시된 청크 수</summary>
        public int CachedChunkCount => chunkCache.Count;

        /// <summary>총 청크 수</summary>
        public int TotalChunkCount => replayData?.ChunkHeaders?.Count ?? 0;

        /// <summary>압축 타입</summary>
        public ReCompressionType CompressionType => compressionType;

        /// <summary>캐시 적중률</summary>
        public float CacheHitRate => (cacheHits + cacheMisses) > 0
            ? (float)cacheHits / (cacheHits + cacheMisses)
            : 0f;

        /// <summary>대기 중인 프리로딩 작업 수</summary>
        public int PendingPreloadCount
        {
            get
            {
                lock (preloadLock)
                {
                    return preloadQueue.Count;
                }
            }
        }

        #endregion

        #region Constructor

        public ChunkStreamManager(string path, ReplayData data, int maxCache = 5)
        {
            filePath = path;
            replayData = data;
            maxCachedChunks = maxCache;
            compressionType = data.Metadata.Compression;

            chunkCache = new ConcurrentDictionary<int, ReplayChunk>();
            lruOrder = new LinkedList<int>();
            lruNodes = new Dictionary<int, LinkedListNode<int>>(maxCache);
            pendingLoads = new ConcurrentDictionary<int, UniTask<ReplayChunk>>();
            preloadQueue = new SortedSet<PreloadTask>(new PreloadTaskComparer());
        }

        #endregion

        #region Public Methods

        /// <summary>파일 스트림을 엽니다.</summary>
        public async UniTask<bool> OpenAsync(CancellationToken ct)
        {
            await UniTask.SwitchToThreadPool();

            try
            {
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

                // 프리로딩 시작
                StartPreloadWorker();

                return true;
            }
            catch (IOException e)
            {
                Debug.LogError($"[ChunkStreamManager] Failed to open file: {e.Message}");
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError($"[ChunkStreamManager] Access denied: {e.Message}");
                return false;
            }
            finally
            {
                await UniTask.SwitchToMainThread(ct);
            }
        }

        /// <summary>특정 시간에 해당하는 청크를 로드합니다.</summary>
        public async UniTask<ReplayChunk> GetChunkAtTimeAsync(float time, CancellationToken ct)
        {
            int chunkIndex = FindChunkIndexForTime(time);

            if (chunkIndex < 0)
            {
                return null;
            }

            return await GetChunkAsync(chunkIndex, ct);
        }

        /// <summary>인덱스로 청크를 로드합니다.</summary>
        public async UniTask<ReplayChunk> GetChunkAsync(int chunkIndex, CancellationToken ct)
        {
            // 캐시 확인
            if (TryGetFromCache(chunkIndex, out var cached))
            {
                cacheHits++;
                return cached;
            }

            cacheMisses++;

            // 범위 검사
            if (chunkIndex < 0 || chunkIndex >= replayData.ChunkHeaders.Count)
            {
                return null;
            }

            // 람다 할당 방지: TryGetValue 먼저 체크
            if (pendingLoads.TryGetValue(chunkIndex, out var existingTask))
            {
                return await existingTask;
            }

            // 새 로딩 작업 생성 및 등록
            var loadTask = LoadChunkInternalAsync(chunkIndex, ct);
            var actualTask = pendingLoads.GetOrAdd(chunkIndex, loadTask);

            // 다른 스레드가 먼저 등록했을 수 있음 - 그 경우 해당 작업 대기
            try
            {
                var chunk = await actualTask;
                return chunk;
            }
            finally
            {
                // 로딩 완료 후 pendingLoads에서 제거
                pendingLoads.TryRemove(chunkIndex, out _);
            }
        }

        /// <summary>선행 로드 (재생 방향 기반 스마트 프리로딩)</summary>
        public void PreloadAdjacentChunksAsync(int currentChunkIndex, CancellationToken ct, bool isReversing = false)
        {
            lock (preloadLock)
            {
                preloadQueue.Clear();

                if (isReversing)
                {
                    // 역재생: 이전 청크 우선
                    if (currentChunkIndex > 0)
                    {
                        EnqueuePreload(currentChunkIndex - 1, PreloadPriority.High);
                    }
                    if (currentChunkIndex > 1)
                    {
                        EnqueuePreload(currentChunkIndex - 2, PreloadPriority.Normal);
                    }
                    if (currentChunkIndex < TotalChunkCount - 1)
                    {
                        EnqueuePreload(currentChunkIndex + 1, PreloadPriority.Low);
                    }
                }
                else
                {
                    // 정재생: 다음 청크 우선
                    if (currentChunkIndex < TotalChunkCount - 1)
                    {
                        EnqueuePreload(currentChunkIndex + 1, PreloadPriority.High);
                    }
                    if (currentChunkIndex < TotalChunkCount - 2)
                    {
                        EnqueuePreload(currentChunkIndex + 2, PreloadPriority.Normal);
                    }
                    if (currentChunkIndex > 0)
                    {
                        EnqueuePreload(currentChunkIndex - 1, PreloadPriority.Low);
                    }
                }
            }

            // 프리로드 워커 깨우기
            WakePreloadWorker();
        }

        /// <summary>특정 청크를 고우선순위로 프리로드 요청</summary>
        public void RequestPreload(int chunkIndex, PreloadPriority priority = PreloadPriority.Normal)
        {
            if (chunkCache.ContainsKey(chunkIndex))
            {
                return; // 이미 캐시에 있음
            }

            lock (preloadLock)
            {
                EnqueuePreload(chunkIndex, priority);
            }

            WakePreloadWorker();
        }

        /// <summary>범위 내 모든 청크를 프리로드</summary>
        public void PreloadRange(int startIndex, int endIndex, PreloadPriority priority = PreloadPriority.Normal)
        {
            startIndex = Mathf.Max(0, startIndex);
            endIndex = Mathf.Min(TotalChunkCount - 1, endIndex);

            lock (preloadLock)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (!chunkCache.ContainsKey(i))
                    {
                        EnqueuePreload(i, priority);
                    }
                }
            }

            WakePreloadWorker();
        }

        /// <summary>캐시를 비웁니다.</summary>
        public void ClearCache()
        {
            lock (preloadLock)
            {
                preloadQueue.Clear();
            }

            lock (lruLock)
            {
                chunkCache.Clear();
                lruOrder.Clear();
                lruNodes.Clear();
            }

            cacheHits = 0;
            cacheMisses = 0;
        }

        /// <summary>통계 정보를 반환합니다.</summary>
        public string GetStatistics()
        {
            return $"Cache: {CachedChunkCount}/{maxCachedChunks}, " +
                   $"Hit Rate: {CacheHitRate:P1}, " +
                   $"Pending: {PendingPreloadCount}, " +
                   $"Compression: {compressionType}";
        }

        #endregion

        #region Private Methods

        private void EnqueuePreload(int chunkIndex, PreloadPriority priority)
        {
            // 이미 캐시에 있거나 로딩 중이면 스킵 (ConcurrentDictionary는 락 없이 안전하게 접근 가능)
            if (chunkCache.ContainsKey(chunkIndex) || pendingLoads.ContainsKey(chunkIndex))
            {
                return;
            }

            // 기존 동일 청크 제거 (우선순위 업데이트용)
            preloadQueue.RemoveWhere(t => t.ChunkIndex == chunkIndex);

            preloadQueue.Add(new PreloadTask
            {
                ChunkIndex = chunkIndex,
                Priority = priority,
                Cts = null
            });
        }

        private void StartPreloadWorker()
        {
            if (isPreloading)
            {
                return;
            }

            preloadCts = new CancellationTokenSource();
            isPreloading = true;
            PreloadWorkerAsync(preloadCts.Token).Forget();
        }

        private void WakePreloadWorker()
        {
            // 워커가 이미 실행 중이므로 별도 처리 불필요
            // 워커는 주기적으로 큐를 확인함
        }

        private async UniTaskVoid PreloadWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !isDisposed)
            {
                PreloadTask? nextTask = null;

                lock (preloadLock)
                {
                    if (preloadQueue.Count > 0)
                    {
                        var task = preloadQueue.Max; // 가장 높은 우선순위
                        preloadQueue.Remove(task);
                        nextTask = task;
                    }
                }

                if (nextTask.HasValue)
                {
                    var task = nextTask.Value;

                    // 이미 캐시에 있으면 스킵
                    if (!chunkCache.ContainsKey(task.ChunkIndex))
                    {
                        try
                        {
                            await LoadChunkInternalAsync(task.ChunkIndex, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[ChunkStreamManager] Preload failed for chunk {task.ChunkIndex}: {e.Message}");
                        }
                    }
                }
                else
                {
                    // 큐가 비었으면 잠시 대기
                    await UniTask.Delay(50, cancellationToken: ct);
                }
            }

            isPreloading = false;
        }

        private bool TryGetFromCache(int chunkIndex, out ReplayChunk chunk)
        {
            if (chunkCache.TryGetValue(chunkIndex, out chunk))
            {
                // LRU 업데이트 (스레드 안전)
                lock (lruLock)
                {
                    if (lruNodes.TryGetValue(chunkIndex, out var node))
                    {
                        lruOrder.Remove(node);
                        lruOrder.AddLast(node);
                    }
                }
                return true;
            }

            chunk = null;
            return false;
        }

        private async UniTask<ReplayChunk> LoadChunkInternalAsync(int chunkIndex, CancellationToken ct)
        {
            var header = replayData.ChunkHeaders[chunkIndex];
            var chunk = await LoadChunkFromFileAsync(header, ct);

            if (chunk != null)
            {
                AddToCache(chunkIndex, chunk);
            }

            return chunk;
        }

        private int FindChunkIndexForTime(float time)
        {
            var headers = replayData.ChunkHeaders;

            // 이진 검색으로 최적화
            int left = 0;
            int right = headers.Count - 1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                var header = headers[mid];

                if (time < header.StartTime)
                {
                    right = mid - 1;
                }
                else if (time > header.EndTime)
                {
                    left = mid + 1;
                }
                else
                {
                    return mid;
                }
            }

            // 범위 밖인 경우
            if (time <= 0 && headers.Count > 0)
            {
                return 0;
            }

            if (headers.Count > 0)
            {
                return headers.Count - 1;
            }

            return -1;
        }

        private async UniTask<ReplayChunk> LoadChunkFromFileAsync(ReplayChunkHeader header, CancellationToken ct)
        {
            await fileAccessSemaphore.WaitAsync(ct);

            try
            {
                await UniTask.SwitchToThreadPool();

                try
                {
                    if (fileStream == null || !fileStream.CanRead)
                    {
                        Debug.LogError("[ChunkStreamManager] File stream is not available");
                        return null;
                    }

                    fileStream.Seek(header.FileOffset, SeekOrigin.Begin);

                    var compressedData = new byte[header.CompressedSize];
                    int bytesRead = await fileStream.ReadAsync(compressedData, 0, header.CompressedSize, ct);

                    if (bytesRead != header.CompressedSize)
                    {
                        Debug.LogWarning($"[ChunkStreamManager] Incomplete read: expected {header.CompressedSize}, got {bytesRead}");
                    }

                    // 압축 해제 (CompressionHelper 사용)
                    byte[] decompressedData = await CompressionHelper.DecompressAsync(compressedData, compressionType, ct);

                    using (var ms = new MemoryStream(decompressedData))
                    using (var reader = new BinaryReader(ms))
                    {
                        return ReplayDeserializer.ReadChunk(reader, header.Index);
                    }
                }
                catch (IOException e)
                {
                    Debug.LogError($"[ChunkStreamManager] Failed to load chunk {header.Index}: {e.Message}");
                    return null;
                }
                catch (InvalidDataException e)
                {
                    Debug.LogError($"[ChunkStreamManager] Corrupted chunk data {header.Index}: {e.Message}");
                    return null;
                }
                finally
                {
                    await UniTask.SwitchToMainThread(ct);
                }
            }
            finally
            {
                fileAccessSemaphore.Release();
            }
        }

        private void AddToCache(int chunkIndex, ReplayChunk chunk)
        {
            lock (lruLock)
            {
                // 캐시 용량 초과 시 LRU 제거
                while (chunkCache.Count >= maxCachedChunks && lruOrder.Count > 0)
                {
                    var oldest = lruOrder.First.Value;
                    lruOrder.RemoveFirst();
                    lruNodes.Remove(oldest);
                    chunkCache.TryRemove(oldest, out _);
                }

                chunkCache[chunkIndex] = chunk;

                if (!lruNodes.ContainsKey(chunkIndex))
                {
                    var node = lruOrder.AddLast(chunkIndex);
                    lruNodes[chunkIndex] = node;
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            preloadCts?.Cancel();
            preloadCts?.Dispose();

            fileStream?.Dispose();
            fileAccessSemaphore?.Dispose();
            chunkLoadSemaphore?.Dispose();

            ClearCache();
        }

        #endregion
    }

    /// <summary>프리로드 작업 비교자 (우선순위 내림차순)</summary>
    internal class PreloadTaskComparer : IComparer<PreloadTask>
    {
        public int Compare(PreloadTask x, PreloadTask y)
        {
            int priorityCompare = y.Priority.CompareTo(x.Priority); // 높은 우선순위 먼저

            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return x.ChunkIndex.CompareTo(y.ChunkIndex); // 같은 우선순위면 인덱스 순
        }
    }
}
