using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Capture;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using ReplaySystem.Network;
using ReplaySystem.Serialization;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplaySystem
{
    /// <summary>리플레이 녹화 관리자 (v2.0)</summary>
    public class ReplayRecorder : MonoBehaviour
    {
        #region Constants

        /// <summary>Job System을 사용하기 위한 최소 오브젝트 수 (이 미만은 순차 처리가 더 효율적)</summary>
        public const int MIN_OBJECTS_FOR_JOBS = 32;

        /// <summary>기본 타겟 프레임 레이트</summary>
        public const int DEFAULT_FRAME_RATE = 60;

        /// <summary>기본 키프레임 간격 (프레임 수)</summary>
        public const int DEFAULT_KEYFRAME_INTERVAL = 60;

        /// <summary>기본 청크당 프레임 수</summary>
        public const int DEFAULT_FRAMES_PER_CHUNK = 300;

        /// <summary>스폰 데이터 바이트 크기 (Position 12bytes + Rotation 16bytes)</summary>
        private const int SPAWN_DATA_SIZE = 28;

        #endregion

        [Header("Settings")]
        [SerializeField, Tooltip("녹화 프레임 레이트 (기본: 60fps)")]
        private int targetFrameRate = DEFAULT_FRAME_RATE;

        [SerializeField, Tooltip("키프레임 생성 간격 (프레임 수, 기본: 60)")]
        private int keyframeInterval = DEFAULT_KEYFRAME_INTERVAL;

        [SerializeField, Tooltip("청크당 프레임 수 (기본: 300, 약 5초 분량)")]
        private int framesPerChunk = DEFAULT_FRAMES_PER_CHUNK;

        [SerializeField, Tooltip("물리(Rigidbody) 상태 캡처 여부")]
        private bool capturePhysics = true;

        [SerializeField, Tooltip("애니메이터 상태 캡처 여부")]
        private bool captureAnimator = true;

        [SerializeField, Tooltip("시작 시 자동으로 모든 캡처 컴포넌트 등록")]
        private bool autoRegisterOnStart = true;

        [Header("Network")]
        [SerializeField, Tooltip("네트워크 이벤트 캡처 여부")]
        private bool captureNetwork;

        [SerializeField] private NetworkReplaySync networkSync;

        [Header("Dynamic Objects")]
        [SerializeField] private DynamicObjectTracker dynamicObjectTracker;

        [Header("Performance")]
        [SerializeField, Tooltip("Job System 사용 여부 (대량 오브젝트 시 성능 향상)")]
        private bool useJobSystem = true;

        [SerializeField, Range(128, 4096), Tooltip("Job System 배치 용량")]
        private int jobBatchCapacity = 1024;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo;

        private readonly List<TransformCapture> trackedTransforms = new List<TransformCapture>();
        private readonly List<RigidbodyCapture> trackedRigidbodies = new List<RigidbodyCapture>();
        private readonly List<AnimatorCapture> trackedAnimators = new List<AnimatorCapture>();
        // ObjectId 기반 룩업 (인덱스 상관관계 의존 제거)
        private readonly Dictionary<int, RigidbodyCapture> rigidbodyLookup = new Dictionary<int, RigidbodyCapture>();
        private readonly Dictionary<int, AnimatorCapture> animatorLookup = new Dictionary<int, AnimatorCapture>();
        private readonly DeltaCompressor deltaCompressor = new DeltaCompressor();

        // Job System 기반 배치 처리기
        private BatchDeltaProcessor batchProcessor;

        // 재사용 버퍼 (GC 할당 최소화)
        private readonly StringBuilder hierarchyPathBuilder = new StringBuilder(256);

        // ReplayFrame 풀링 (GC 할당 최소화)
        private readonly Queue<ReplayFrame> framePool = new Queue<ReplayFrame>();
        private readonly Queue<List<CompressedTransformDelta>> transformDeltaListPool = new Queue<List<CompressedTransformDelta>>();
        private readonly Queue<List<CompressedRigidbodyDelta>> rigidbodyDeltaListPool = new Queue<List<CompressedRigidbodyDelta>>();
        private readonly Queue<List<CompressedAnimatorDelta>> animatorDeltaListPool = new Queue<List<CompressedAnimatorDelta>>();

        // SerializeSpawnData 재사용 버퍼 (Position 12bytes + Rotation 16bytes = 28bytes)
        private readonly byte[] spawnDataBuffer = new byte[28];

        private ReplayData currentRecording;
        private ReplayChunk currentChunk;
        private CancellationTokenSource recordingCts;
        private string tempChunkPath;

        private float recordingStartTime;
        private int frameCounter;
        private int nextObjectId;
        private int currentChunkIndex;
        private bool isRecording;

        // Job System 비동기 처리용 (블로킹 방지)
        private JobHandle pendingJobHandle;
        private ReplayFrame pendingFrame;
        private bool hasPendingJob;

        /// <summary>녹화 중 여부</summary>
        public bool IsRecording => isRecording;

        /// <summary>현재 녹화 시간</summary>
        public float CurrentRecordingTime => isRecording ? Time.time - recordingStartTime : 0f;

        /// <summary>추적 오브젝트 수</summary>
        public int TrackedObjectCount => trackedTransforms.Count;

        /// <summary>녹화 시작 이벤트</summary>
        public event Action OnRecordingStarted;

        /// <summary>녹화 중지 이벤트</summary>
        public event Action<ReplayData> OnRecordingStopped;

        private void Start()
        {
            if (autoRegisterOnStart)
            {
                RegisterAllCaptures();
            }
        }

        private void OnDestroy()
        {
            StopRecording();
            batchProcessor?.Dispose();
            CleanupTempFiles(); // 리소스 누수 방지
            ClearFramePool();
        }

        /// <summary>씬 내 모든 캡처 컴포넌트를 등록합니다.</summary>
        public void RegisterAllCaptures()
        {
            var transforms = FindObjectsByType<TransformCapture>(FindObjectsSortMode.None);

            foreach (var capture in transforms)
            {
                RegisterTransform(capture);
            }
        }

        /// <summary>Transform 캡처를 등록합니다.</summary>
        public int RegisterTransform(TransformCapture capture)
        {
            if (capture == null || trackedTransforms.Contains(capture))
            {
                return capture?.ObjectId ?? -1;
            }

            int id = nextObjectId++;
            capture.Initialize(id);
            trackedTransforms.Add(capture);

            var rbCapture = capture.GetComponent<RigidbodyCapture>();

            if (rbCapture != null && capturePhysics)
            {
                trackedRigidbodies.Add(rbCapture);
                rigidbodyLookup[id] = rbCapture; // ObjectId 기반 룩업 등록
            }

            var animCapture = capture.GetComponent<AnimatorCapture>();

            if (animCapture != null && captureAnimator)
            {
                trackedAnimators.Add(animCapture);
                animatorLookup[id] = animCapture; // ObjectId 기반 룩업 등록

                // EventCapture에 등록하여 Animation 이벤트 충돌 방지
                EventCapture.Instance.RegisterAnimatorCaptureObject(id);
            }

            return id;
        }

        /// <summary>IReplayCapture 인터페이스로 등록</summary>
        public int RegisterObject(IReplayCapture capture)
        {
            if (capture is TransformCapture tc)
            {
                return RegisterTransform(tc);
            }

            return -1;
        }

        /// <summary>녹화를 시작합니다.</summary>
        public void StartRecording()
        {
            if (isRecording)
            {
                Debug.LogWarning("[ReplayRecorder] Already recording");
                return;
            }

            isRecording = true;
            recordingCts = new CancellationTokenSource();
            recordingStartTime = Time.time;
            frameCounter = 0;
            currentChunkIndex = 0;

            tempChunkPath = Path.Combine(Application.temporaryCachePath, $"replay_chunks_{DateTime.Now.Ticks}");
            Directory.CreateDirectory(tempChunkPath);

            currentRecording = new ReplayData
            {
                Metadata = new ReplayMetadata
                {
                    RecordedAtTicks = DateTime.UtcNow.Ticks,
                    FrameRate = targetFrameRate,
                    KeyframeInterval = keyframeInterval,
                    FramesPerChunk = framesPerChunk,
                    SceneName = SceneManager.GetActiveScene().name,
                    TrackedObjectCount = trackedTransforms.Count,
                    HasPhysicsData = capturePhysics,
                    HasNetworkData = captureNetwork,
                    HasAnimatorData = captureAnimator && trackedAnimators.Count > 0,
                    Compression = Core.ReCompressionType.GZip
                },
                InitialState = CaptureInitialState()
            };

            StartNewChunk(true);
            deltaCompressor.Reset();

            // Job System 초기화
            if (useJobSystem)
            {
                batchProcessor?.Dispose();
                batchProcessor = new BatchDeltaProcessor(
                    Math.Max(trackedTransforms.Count, jobBatchCapacity));
                batchProcessor.Reset();
            }

            if (dynamicObjectTracker != null)
            {
                dynamicObjectTracker.SetRecorder(this);
                dynamicObjectTracker.SetRecordingState(true);
            }

            if (networkSync != null && captureNetwork)
            {
                networkSync.SetRecordingState(true);
            }

            EventCapture.Instance.SetRecordingState(true, 0f, 0);

            RecordingLoopAsync(recordingCts.Token).Forget();

            OnRecordingStarted?.Invoke();

            Debug.Log($"[ReplayRecorder] Recording started - {trackedTransforms.Count} transforms, {trackedRigidbodies.Count} rigidbodies, {trackedAnimators.Count} animators");
        }

        /// <summary>녹화를 중지하고 데이터를 반환합니다.</summary>
        public ReplayData StopRecording()
        {
            if (!isRecording)
            {
                return null;
            }

            isRecording = false;
            recordingCts?.Cancel();
            recordingCts?.Dispose();
            recordingCts = null;

            // 대기 중인 Job 완료 (데이터 손실 방지)
            if (hasPendingJob)
            {
                CompletePendingJob();
            }

            FinalizeCurrentChunk();

            currentRecording.Metadata.Duration = Time.time - recordingStartTime;
            currentRecording.Metadata.ChunkCount = currentChunkIndex;

            var pendingEvents = EventCapture.Instance.FlushEvents();
            currentRecording.Events.AddRange(pendingEvents);

            if (dynamicObjectTracker != null)
            {
                dynamicObjectTracker.SetRecordingState(false);
            }

            if (networkSync != null)
            {
                networkSync.SetRecordingState(false);
            }

            EventCapture.Instance.SetRecordingState(false, 0f, 0);
            EventCapture.Instance.ClearAnimatorCaptureObjects();

            var result = currentRecording;
            currentRecording = null;

            Debug.Log($"[ReplayRecorder] Recording stopped - {currentChunkIndex} chunks, {result.Events.Count} events");

            return result;
        }

        /// <summary>녹화를 취소하고 임시 파일을 정리합니다 (데이터 반환 없음).</summary>
        public void CancelRecording()
        {
            StopRecording();
            CleanupTempFiles();
            ClearFramePool();

            Debug.Log("[ReplayRecorder] Recording cancelled and temp files cleaned up");
        }

        /// <summary>녹화 데이터를 파일로 저장합니다.</summary>
        public async UniTask SaveRecordingAsync(string filePath, CancellationToken ct)
        {
            var data = StopRecording();

            if (data == null)
            {
                Debug.LogWarning("[ReplayRecorder] No recording data to save");
                return;
            }

            await SaveToFileAsync(data, filePath, ct);
            CleanupTempFiles();

            Debug.Log($"[ReplayRecorder] Recording saved to: {filePath}");
        }

        private async UniTask SaveToFileAsync(ReplayData data, string filePath, CancellationToken ct)
        {
            await UniTask.SwitchToThreadPool();

            try
            {
                var directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                using (var gzip = new GZipStream(fileStream, System.IO.Compression.CompressionLevel.Optimal))
                using (var writer = new BinaryWriter(gzip))
                {
                    // ReplaySerializer의 public 메서드 사용 (SRP 준수)
                    ReplaySerializer.WriteHeader(writer, data);
                    ReplaySerializer.WriteMetadata(writer, data.Metadata);
                    ReplaySerializer.WriteInitialState(writer, data.InitialState);

                    // 청크 헤더 및 데이터 (스트리밍 방식 - 임시 파일에서 통합)
                    writer.Write(data.ChunkHeaders.Count);

                    foreach (var header in data.ChunkHeaders)
                    {
                        string chunkFilePath = Path.Combine(tempChunkPath, $"chunk_{header.Index}.bin");

                        if (File.Exists(chunkFilePath))
                        {
                            var chunkData = File.ReadAllBytes(chunkFilePath);
                            writer.Write(header.Index);
                            writer.Write(header.StartFrame);
                            writer.Write(header.EndFrame);
                            writer.Write(header.StartTime);
                            writer.Write(header.EndTime);
                            writer.Write(fileStream.Position);
                            writer.Write(chunkData.Length);
                            writer.Write(header.ContainsKeyframe);
                            writer.Write(chunkData);
                        }
                    }

                    ReplaySerializer.WriteEvents(writer, data.Events);
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread(ct);
            }
        }

        // NOTE: WriteMetadata, WriteInitialState, WriteEvents는 ReplaySerializer로 이동됨
        // ReplaySerializer.WriteMetadata(), ReplaySerializer.WriteInitialState(), ReplaySerializer.WriteEvents() 사용

        private SceneSnapshot CaptureInitialState()
        {
            var snapshot = new SceneSnapshot();

            foreach (var capture in trackedTransforms)
            {
                if (capture == null)
                {
                    continue;
                }

                var t = capture.transform;
                var rbCapture = capture.GetComponent<RigidbodyCapture>();

                var state = new ObjectState
                {
                    Id = capture.ObjectId,
                    PrefabName = capture.PrefabName,
                    PrefabPath = capture.PrefabPath,
                    HierarchyPath = GetHierarchyPath(t),
                    IsNetworkObject = DetectNetworkObject(capture.gameObject),
                    Transform = CompressedTransform.FromTransform(t, capture.gameObject.activeSelf),
                    HasRigidbody = rbCapture != null && rbCapture.HasRigidbody
                };

                if (state.HasRigidbody)
                {
                    state.Rigidbody = rbCapture.CaptureState();
                }

                // Animator 초기 상태 캡처
                if (captureAnimator && animatorLookup.TryGetValue(capture.ObjectId, out var animCapture))
                {
                    if (animCapture != null && animCapture.HasAnimator)
                    {
                        state.HasAnimator = true;
                        state.Animator = animCapture.CaptureState();
                    }
                }

                snapshot.Objects.Add(state);
            }

            if (dynamicObjectTracker != null)
            {
                dynamicObjectTracker.PopulateSnapshot(snapshot);
            }

            return snapshot;
        }

        private void StartNewChunk(bool withKeyframe)
        {
            currentChunk = new ReplayChunk
            {
                Index = currentChunkIndex
            };

            if (withKeyframe)
            {
                currentChunk.Keyframe = CaptureKeyframe();
            }

            var header = new ReplayChunkHeader
            {
                Index = currentChunkIndex,
                StartFrame = frameCounter,
                StartTime = Time.time - recordingStartTime,
                ContainsKeyframe = withKeyframe
            };

            currentRecording.ChunkHeaders.Add(header);
        }

        private void FinalizeCurrentChunk()
        {
            if (currentChunk == null)
            {
                return;
            }

            int headerIndex = currentRecording.ChunkHeaders.FindIndex(h => h.Index == currentChunk.Index);

            if (headerIndex >= 0)
            {
                var header = currentRecording.ChunkHeaders[headerIndex];
                header.EndFrame = frameCounter;
                header.EndTime = Time.time - recordingStartTime;
                currentRecording.ChunkHeaders[headerIndex] = header;
            }

            SaveChunkToTempFile(currentChunk);

            // 저장 완료 후 프레임을 풀에 반환 (GC 할당 최소화)
            foreach (var frame in currentChunk.Frames)
            {
                ReturnFrameToPool(frame);
            }
            currentChunk.Frames.Clear();

            currentChunkIndex++;
        }

        private void SaveChunkToTempFile(ReplayChunk chunk)
        {
            string chunkFilePath = Path.Combine(tempChunkPath, $"chunk_{chunk.Index}.bin");

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                ReplaySerializer.WriteChunk(writer, chunk);
                File.WriteAllBytes(chunkFilePath, ms.ToArray());
            }
        }

        private async UniTaskVoid RecordingLoopAsync(CancellationToken ct)
        {
            float frameInterval = 1f / targetFrameRate;
            float nextFrameTime = 0f;

            try
            {
                while (!ct.IsCancellationRequested && isRecording)
                {
                    float currentTime = Time.time - recordingStartTime;

                    if (currentTime >= nextFrameTime)
                    {
                        CaptureFrame(currentTime);
                        nextFrameTime += frameInterval;

                        if (currentChunk.Frames.Count >= framesPerChunk)
                        {
                            FinalizeCurrentChunk();
                            StartNewChunk(true);
                        }
                    }

                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 취소 - 무시
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayRecorder] Recording loop error: {e.Message}\n{e.StackTrace}");
                isRecording = false;
            }
        }

        private ReplayKeyframe CaptureKeyframe()
        {
            var keyframe = new ReplayKeyframe
            {
                FrameIndex = frameCounter,
                Time = Time.time - recordingStartTime,
                States = new List<CompressedObjectState>(trackedTransforms.Count)
            };

            foreach (var capture in trackedTransforms)
            {
                if (capture == null)
                {
                    continue;
                }

                var t = capture.transform;
                var rbCapture = capture.GetComponent<RigidbodyCapture>();

                var state = new CompressedObjectState
                {
                    ObjectId = capture.ObjectId,
                    Transform = CompressedTransform.FromTransform(t, capture.gameObject.activeSelf),
                    HasRigidbody = rbCapture != null && rbCapture.HasRigidbody
                };

                if (state.HasRigidbody)
                {
                    state.Rigidbody = rbCapture.CaptureState();
                }

                // Animator 키프레임 상태 캡처
                if (captureAnimator && animatorLookup.TryGetValue(capture.ObjectId, out var animCapture))
                {
                    if (animCapture != null && animCapture.HasAnimator)
                    {
                        state.HasAnimator = true;
                        state.Animator = animCapture.CaptureState();
                    }
                }

                keyframe.States.Add(state);
            }

            return keyframe;
        }

        private void CaptureFrame(float time)
        {
            EventCapture.Instance.UpdateTime(time, frameCounter);

            uint networkTick = networkSync?.CurrentTick ?? 0;

            // 풀에서 ReplayFrame 및 List 가져오기 (GC 할당 최소화)
            var frame = GetPooledFrame();
            frame.Index = frameCounter;
            frame.Time = time;
            frame.DeltaTime = Time.deltaTime;
            frame.NetworkTick = networkTick;
            frame.TransformDeltas.Clear();
            frame.RigidbodyDeltas.Clear();
            frame.AnimatorDeltas?.Clear();

            // Job System 사용 여부에 따라 분기 (오브젝트 수가 적으면 순차 처리가 더 효율적)
            if (useJobSystem && batchProcessor != null && trackedTransforms.Count >= MIN_OBJECTS_FOR_JOBS)
            {
                CaptureFrameWithJobs(frame);
            }
            else
            {
                CaptureFrameSequential(frame);
            }

            var events = EventCapture.Instance.FlushEvents();
            currentChunk.Events.AddRange(events);

            if (dynamicObjectTracker != null)
            {
                var dynamicEvents = dynamicObjectTracker.FlushEvents();

                foreach (var dynEvt in dynamicEvents)
                {
                    var replayEvent = new ReplayEvent
                    {
                        Time = dynEvt.Time - recordingStartTime,
                        FrameIndex = frameCounter,
                        TargetObjectId = dynEvt.ObjectId,
                        EventType = dynEvt.Type == DynamicEventType.Create ? ReplayEventType.Instantiate : ReplayEventType.Destroy,
                        NetworkTick = networkTick
                    };

                    if (dynEvt.Type == DynamicEventType.Create)
                    {
                        replayEvent.EventName = dynEvt.PrefabHash.ToString();
                        replayEvent.Parameters = SerializeSpawnData(dynEvt.Position, dynEvt.Rotation);
                    }

                    currentChunk.Events.Add(replayEvent);
                }
            }

            if (networkSync != null && captureNetwork)
            {
                var netEvents = networkSync.FlushEvents();

                foreach (var netEvt in netEvents)
                {
                    // 프레임 인덱스를 명시적으로 전달하여 틱/프레임 동기화 정확도 향상
                    currentChunk.Events.Add(networkSync.ConvertToReplayEvent(netEvt, time, frameCounter));
                }
            }

            currentChunk.Frames.Add(frame);
            frameCounter++;
        }

        /// <summary>Job System을 사용한 프레임 캡처 (비블로킹 병렬 처리)</summary>
        private void CaptureFrameWithJobs(ReplayFrame frame)
        {
            // 이전 프레임의 Job이 있으면 완료 대기 및 결과 수집
            if (hasPendingJob)
            {
                CompletePendingJob();
            }

            int count = trackedTransforms.Count;
            batchProcessor.SetActiveCount(count);

            // 1단계: 데이터 수집 (메인 스레드)
            CollectTransformData(count);

            // 2단계: 델타 계산 Job 스케줄 (블로킹 없이)
            var computeHandle = batchProcessor.ScheduleDeltaComputation();

            // 3단계: 버퍼 스왑 Job 스케줄 (compute 완료 후 실행되도록 의존성 설정)
            pendingJobHandle = batchProcessor.SwapBuffers(computeHandle);

            // 현재 프레임 정보 저장 (다음 프레임에서 결과 수집 시 사용)
            pendingFrame = frame;
            hasPendingJob = true;

            // 즉시 결과가 필요한 경우 (첫 프레임 또는 청크 마무리 직전)
            // 이 경우에만 동기 대기
            if (currentChunk.Frames.Count == 0 || currentChunk.Frames.Count >= framesPerChunk - 1)
            {
                CompletePendingJob();
            }
        }

        /// <summary>대기 중인 Job 완료 및 결과 수집</summary>
        private void CompletePendingJob()
        {
            // hasPendingJob 플래그만으로 충분 (pendingFrame은 struct이므로 null 체크 불필요)
            if (!hasPendingJob)
            {
                return;
            }

            // Job 완료 대기
            pendingJobHandle.Complete();

            // 결과 수집 (pendingFrame.TransformDeltas는 hasPendingJob=true일 때 항상 유효)
            if (pendingFrame.TransformDeltas != null)
            {
                batchProcessor.CollectResults(
                    (id, delta) => pendingFrame.TransformDeltas.Add(delta),
                    (id, delta) => pendingFrame.RigidbodyDeltas.Add(delta)
                );
            }

            hasPendingJob = false;
        }

        /// <summary>Transform 데이터 수집 (메인 스레드)</summary>
        private void CollectTransformData(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var capture = trackedTransforms[i];

                if (capture == null)
                {
                    continue;
                }

                var t = capture.transform;
                batchProcessor.SetTransformData(
                    i,
                    capture.ObjectId,
                    t.position,
                    t.rotation,
                    t.localScale,
                    capture.gameObject.activeSelf
                );

                // Rigidbody 데이터 (Dictionary 룩업으로 인덱스 상관관계 의존 제거)
                if (capturePhysics && rigidbodyLookup.TryGetValue(capture.ObjectId, out var rbCapture))
                {
                    if (rbCapture != null && rbCapture.HasRigidbody)
                    {
                        var rb = rbCapture.CachedRigidbody;
                        batchProcessor.SetRigidbodyData(i, rb.linearVelocity, rb.angularVelocity);
                    }
                    else
                    {
                        batchProcessor.SetNoRigidbody(i);
                    }
                }
                else
                {
                    batchProcessor.SetNoRigidbody(i);
                }
            }
        }

        /// <summary>순차적 프레임 캡처 (기존 방식)</summary>
        private void CaptureFrameSequential(ReplayFrame frame)
        {
            foreach (var capture in trackedTransforms)
            {
                if (capture == null)
                {
                    continue;
                }

                var t = capture.transform;
                var compressed = CompressedTransform.FromTransform(t, capture.gameObject.activeSelf);
                var delta = deltaCompressor.ComputeTransformDelta(capture.ObjectId, compressed);

                if (delta.HasValue)
                {
                    frame.TransformDeltas.Add(delta.Value);
                }
            }

            if (capturePhysics)
            {
                foreach (var rbCapture in trackedRigidbodies)
                {
                    if (rbCapture == null || !rbCapture.HasRigidbody)
                    {
                        continue;
                    }

                    var delta = rbCapture.CaptureDelta();

                    if (delta.HasValue)
                    {
                        frame.RigidbodyDeltas.Add(delta.Value);
                    }
                }
            }

            // Animator 델타 캡처
            if (captureAnimator)
            {
                foreach (var animCapture in trackedAnimators)
                {
                    if (animCapture == null || !animCapture.HasAnimator)
                    {
                        continue;
                    }

                    var delta = animCapture.CaptureDelta();

                    if (delta.HasValue)
                    {
                        frame.AnimatorDeltas.Add(delta.Value);
                    }
                }
            }
        }

        private byte[] SerializeSpawnData(Vector3 position, Quaternion rotation)
        {
            // 재사용 버퍼에 직접 쓰기 (GC 할당 최소화)
            int offset = 0;

            WriteFloatDirect(spawnDataBuffer, ref offset, position.x);
            WriteFloatDirect(spawnDataBuffer, ref offset, position.y);
            WriteFloatDirect(spawnDataBuffer, ref offset, position.z);
            WriteFloatDirect(spawnDataBuffer, ref offset, rotation.x);
            WriteFloatDirect(spawnDataBuffer, ref offset, rotation.y);
            WriteFloatDirect(spawnDataBuffer, ref offset, rotation.z);
            WriteFloatDirect(spawnDataBuffer, ref offset, rotation.w);

            // 반환 시에는 복사본 생성 (이벤트 데이터로 저장되므로)
            // 스폰 이벤트는 드물게 발생하므로 이 할당은 허용
            var result = new byte[28];
            Buffer.BlockCopy(spawnDataBuffer, 0, result, 0, 28);
            return result;
        }

        /// <summary>Float를 바이트 배열에 직접 쓰기 (BitConverter.GetBytes 할당 회피)</summary>
        private static void WriteFloatDirect(byte[] buffer, ref int offset, float value)
        {
            // Union 패턴으로 float → int 변환 후 직접 바이트 쓰기
            var union = new FloatIntUnion { FloatValue = value };
            buffer[offset] = (byte)union.IntValue;
            buffer[offset + 1] = (byte)(union.IntValue >> 8);
            buffer[offset + 2] = (byte)(union.IntValue >> 16);
            buffer[offset + 3] = (byte)(union.IntValue >> 24);
            offset += 4;
        }

        /// <summary>Float-Int Union (비트 변환용)</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public float FloatValue;
            [System.Runtime.InteropServices.FieldOffset(0)]
            public int IntValue;
        }

        #region Frame Pooling

        private ReplayFrame GetPooledFrame()
        {
            if (framePool.Count > 0)
            {
                return framePool.Dequeue();
            }

            return new ReplayFrame
            {
                TransformDeltas = GetPooledTransformDeltaList(),
                RigidbodyDeltas = GetPooledRigidbodyDeltaList(),
                AnimatorDeltas = GetPooledAnimatorDeltaList()
            };
        }

        private List<CompressedTransformDelta> GetPooledTransformDeltaList()
        {
            if (transformDeltaListPool.Count > 0)
            {
                return transformDeltaListPool.Dequeue();
            }

            return new List<CompressedTransformDelta>(trackedTransforms.Count > 0 ? trackedTransforms.Count : 64);
        }

        private List<CompressedRigidbodyDelta> GetPooledRigidbodyDeltaList()
        {
            if (rigidbodyDeltaListPool.Count > 0)
            {
                return rigidbodyDeltaListPool.Dequeue();
            }

            return new List<CompressedRigidbodyDelta>(trackedRigidbodies.Count > 0 ? trackedRigidbodies.Count : 32);
        }

        private List<CompressedAnimatorDelta> GetPooledAnimatorDeltaList()
        {
            if (animatorDeltaListPool.Count > 0)
            {
                return animatorDeltaListPool.Dequeue();
            }

            return new List<CompressedAnimatorDelta>(trackedAnimators.Count > 0 ? trackedAnimators.Count : 16);
        }

        private void ReturnFrameToPool(ReplayFrame frame)
        {
            frame.TransformDeltas.Clear();
            frame.RigidbodyDeltas.Clear();
            frame.AnimatorDeltas?.Clear();
            framePool.Enqueue(frame);
        }

        private void ClearFramePool()
        {
            framePool.Clear();
            transformDeltaListPool.Clear();
            animatorDeltaListPool.Clear();
            rigidbodyDeltaListPool.Clear();
        }

        #endregion

        private string GetHierarchyPath(Transform t)
        {
            hierarchyPathBuilder.Clear();
            BuildHierarchyPathRecursive(t, hierarchyPathBuilder);
            return hierarchyPathBuilder.ToString();
        }

        private static Type _networkObjectType;
        private static bool _networkObjectTypeResolved;

        private static Type GetNetworkObjectType()
        {
            if (_networkObjectTypeResolved)
            {
                return _networkObjectType;
            }
            _networkObjectType = Type.GetType("FishNet.Object.NetworkObject, FishNet.Runtime");
            _networkObjectTypeResolved = true;
            return _networkObjectType;
        }

        private static bool DetectNetworkObject(GameObject go)
        {
            if (go == null)
            {
                return false;
            }
            var type = GetNetworkObjectType();
            return type != null && go.GetComponent(type) != null;
        }

        private static void BuildHierarchyPathRecursive(Transform t, StringBuilder sb)
        {
            if (t.parent != null)
            {
                BuildHierarchyPathRecursive(t.parent, sb);
                sb.Append('/');
            }

            sb.Append(t.name);
        }

        private void CleanupTempFiles()
        {
            if (Directory.Exists(tempChunkPath))
            {
                try
                {
                    Directory.Delete(tempChunkPath, true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ReplayRecorder] Failed to cleanup temp files: {e.Message}");
                }
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !isRecording)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 350, 150));
            GUILayout.Label($"[RECORDING] Time: {CurrentRecordingTime:F2}s");
            GUILayout.Label($"Frame: {frameCounter} | Chunk: {currentChunkIndex}");
            GUILayout.Label($"Transforms: {trackedTransforms.Count} | RBs: {trackedRigidbodies.Count} | Anims: {trackedAnimators.Count}");
            GUILayout.Label($"Chunk Frames: {currentChunk?.Frames.Count ?? 0}/{framesPerChunk}");
            GUILayout.Label($"Events: {currentChunk?.Events.Count ?? 0}");

            if (captureNetwork && networkSync != null)
            {
                GUILayout.Label($"Network Tick: {networkSync.CurrentTick}");
            }

            GUILayout.EndArea();
        }
    }
}
