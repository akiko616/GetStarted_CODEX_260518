using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Capture;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using ReplaySystem.Network;
using ReplaySystem.Serialization;
using UnityEngine;

namespace ReplaySystem.Playback
{
    /// <summary>리플레이 재생 상태</summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused,
        Reversing
    }

    /// <summary>리플레이 재생 관리자 (v2.0)</summary>
    public class ReplayPlayer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float playbackSpeed = 1f;
        [SerializeField] private bool interpolateTransforms = true;
        [SerializeField] private bool interpolatePhysics = true;
        [SerializeField] private bool interpolateAnimators = true;

        [Header("References")]
        [SerializeField] private DynamicObjectTracker dynamicObjectTracker;
        [SerializeField] private NetworkReplaySync networkSync;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo;

        private readonly Dictionary<int, TransformCapture> transformMap = new Dictionary<int, TransformCapture>();
        private readonly Dictionary<int, RigidbodyCapture> rigidbodyMap = new Dictionary<int, RigidbodyCapture>();
        private readonly Dictionary<int, AnimatorCapture> animatorMap = new Dictionary<int, AnimatorCapture>();
        private readonly Dictionary<int, CompressedObjectState> reconstructedStates = new Dictionary<int, CompressedObjectState>();
        private readonly Dictionary<int, CompressedObjectState> previousStates = new Dictionary<int, CompressedObjectState>();

        // 이벤트 동기화용
        private readonly HashSet<int> executedEventIndices = new HashSet<int>();
        private float lastEventProcessTime;

        // 역재생 시 파괴된 오브젝트 복원용 캐시
        private readonly Dictionary<int, DestroyedObjectCache> destroyedObjectsCache = new Dictionary<int, DestroyedObjectCache>();

        private ReplayData loadedReplay;
        private ChunkStreamManager chunkManager;
        private ReplayChunk currentChunk;
        private CancellationTokenSource playbackCts;

        private PlaybackState currentState = PlaybackState.Stopped;
        private float currentTime;
        private int currentFrameIndex;
        private int currentChunkIndex;

        private INetworkReplayHandler networkHandler;

        /// <summary>ObjectId로 매핑된 GameObject를 조회합니다 (네트워크 이벤트 핸들러용).</summary>
        public GameObject ResolveObject(int objectId)
        {
            return transformMap.TryGetValue(objectId, out var capture) && capture != null
                ? capture.gameObject
                : null;
        }

        /// <summary>현재 재생 상태</summary>
        public PlaybackState CurrentState => currentState;

        /// <summary>현재 재생 시간</summary>
        public float CurrentTime => currentTime;

        /// <summary>총 재생 시간</summary>
        public float Duration => loadedReplay?.Metadata.Duration ?? 0f;

        /// <summary>재생 진행률 (0~1)</summary>
        public float Progress => Duration > 0 ? currentTime / Duration : 0f;

        /// <summary>재생 속도</summary>
        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set => playbackSpeed = Mathf.Clamp(value, -4f, 4f);
        }

        /// <summary>리플레이 로드 완료 이벤트</summary>
        public event Action<ReplayData> OnReplayLoaded;

        /// <summary>재생 상태 변경 이벤트</summary>
        public event Action<PlaybackState> OnStateChanged;

        /// <summary>재생 시간 변경 이벤트</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>재생 완료 이벤트</summary>
        public event Action OnPlaybackComplete;

        private void OnDestroy()
        {
            Stop();
            chunkManager?.Dispose();
        }

        /// <summary>네트워크 리플레이 핸들러 설정</summary>
        public void SetNetworkHandler(INetworkReplayHandler handler)
        {
            networkHandler = handler;
        }

        /// <summary>파일에서 리플레이를 로드합니다.</summary>
        /// <returns>로드 성공 여부</returns>
        public async UniTask<bool> LoadAsync(string filePath, CancellationToken ct)
        {
            chunkManager?.Dispose();

            var loadResult = await ReplayDeserializer.LoadHeaderAsync(filePath, ct);

            if (!loadResult.Success)
            {
                Debug.LogError($"[ReplayPlayer] Failed to load replay: {loadResult.ErrorMessage}");
                return false;
            }

            loadedReplay = loadResult.Data;
            chunkManager = new ChunkStreamManager(filePath, loadedReplay);

            bool openSuccess = await chunkManager.OpenAsync(ct);

            if (!openSuccess)
            {
                Debug.LogError("[ReplayPlayer] Failed to open chunk stream");
                loadedReplay = null;
                chunkManager?.Dispose();
                chunkManager = null;
                return false;
            }

            OnReplayLoaded?.Invoke(loadedReplay);

            Debug.Log($"[ReplayPlayer] Loaded replay: {loadedReplay.Metadata.Duration:F2}s, {loadedReplay.Metadata.ChunkCount} chunks");
            return true;
        }

        /// <summary>오브젝트 자동 매핑 (HierarchyPath 기반 정확 매칭 + 중복 방지)</summary>
        public void AutoMapObjects()
        {
            if (loadedReplay?.InitialState == null)
            {
                return;
            }

            transformMap.Clear();
            rigidbodyMap.Clear();
            animatorMap.Clear();

            var captures = FindObjectsByType<TransformCapture>(FindObjectsSortMode.None);
            var matched = new HashSet<TransformCapture>();

            foreach (var objState in loadedReplay.InitialState.Objects)
            {
                TransformCapture bestMatch = null;

                foreach (var capture in captures)
                {
                    if (matched.Contains(capture)) continue;

                    bool nameMatch = capture.PrefabName == objState.PrefabName ||
                                     capture.gameObject.name == objState.PrefabName;

                    if (!nameMatch) continue;

                    // HierarchyPath까지 일치하면 정확 매칭 → 즉시 선택
                    if (!string.IsNullOrEmpty(objState.HierarchyPath))
                    {
                        string capturePath = capture.GetHierarchyPath();
                        if (capturePath == objState.HierarchyPath)
                        {
                            bestMatch = capture;
                            break;
                        }
                    }

                    // 이름만 일치하면 후보로 저장 (더 정확한 매칭이 없을 때 사용)
                    if (bestMatch == null)
                    {
                        bestMatch = capture;
                    }
                }

                if (bestMatch != null)
                {
                    matched.Add(bestMatch);
                    bestMatch.Initialize(objState.Id);
                    transformMap[objState.Id] = bestMatch;

                    var rbCapture = bestMatch.GetComponent<RigidbodyCapture>();

                    if (rbCapture != null && objState.HasRigidbody)
                    {
                        rigidbodyMap[objState.Id] = rbCapture;
                    }

                    var animCapture = bestMatch.GetComponent<AnimatorCapture>();

                    if (animCapture != null && objState.HasAnimator)
                    {
                        animatorMap[objState.Id] = animCapture;
                    }
                }
            }

            Debug.Log($"[ReplayPlayer] Auto-mapped {transformMap.Count} transforms, {rigidbodyMap.Count} rigidbodies, {animatorMap.Count} animators");
        }

        /// <summary>Transform 캡처 수동 등록</summary>
        public void RegisterTransform(int objectId, TransformCapture capture)
        {
            transformMap[objectId] = capture;

            var rbCapture = capture.GetComponent<RigidbodyCapture>();

            if (rbCapture != null)
            {
                rigidbodyMap[objectId] = rbCapture;
            }

            var animCapture = capture.GetComponent<AnimatorCapture>();

            if (animCapture != null)
            {
                animatorMap[objectId] = animCapture;
            }
        }

        /// <summary>재생을 시작합니다.</summary>
        public void Play()
        {
            if (loadedReplay == null)
            {
                Debug.LogWarning("[ReplayPlayer] No replay loaded");
                return;
            }

            if (currentState == PlaybackState.Paused)
            {
                SetState(PlaybackState.Playing);
                return;
            }

            Stop();
            playbackCts = new CancellationTokenSource();

            ApplyInitialState();
            SetState(PlaybackState.Playing);

            PlaybackLoopAsync(playbackCts.Token).Forget();
        }

        /// <summary>역재생을 시작합니다.</summary>
        public void PlayReverse()
        {
            if (loadedReplay == null)
            {
                Debug.LogWarning("[ReplayPlayer] No replay loaded");
                return;
            }

            if (currentState == PlaybackState.Stopped)
            {
                SeekToTime(Duration);
                playbackCts = new CancellationTokenSource();
                PlaybackLoopAsync(playbackCts.Token).Forget();
            }

            SetState(PlaybackState.Reversing);
        }

        /// <summary>일시정지</summary>
        public void Pause()
        {
            if (currentState == PlaybackState.Playing || currentState == PlaybackState.Reversing)
            {
                SetState(PlaybackState.Paused);
            }
        }

        /// <summary>재생 중지</summary>
        public void Stop()
        {
            playbackCts?.Cancel();
            playbackCts?.Dispose();
            playbackCts = null;

            currentTime = 0f;
            currentFrameIndex = 0;
            currentChunkIndex = 0;
            currentChunk = null;
            reconstructedStates.Clear();
            previousStates.Clear();
            executedEventIndices.Clear();
            destroyedObjectsCache.Clear();
            lastEventProcessTime = 0f;

            SetState(PlaybackState.Stopped);
        }

        /// <summary>재생/일시정지 토글</summary>
        public void TogglePlayPause()
        {
            switch (currentState)
            {
                case PlaybackState.Playing:
                case PlaybackState.Reversing:
                    Pause();
                    break;
                default:
                    Play();
                    break;
            }
        }

        /// <summary>특정 시간으로 이동</summary>
        public void SeekToTime(float time)
        {
            SeekToTimeAsync(time, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>특정 시간으로 이동 (비동기)</summary>
        public async UniTask SeekToTimeAsync(float time, CancellationToken ct)
        {
            if (loadedReplay == null)
            {
                return;
            }

            time = Mathf.Clamp(time, 0f, Duration);
            currentTime = time;

            int targetChunkIndex = FindChunkIndexForTime(time);

            if (targetChunkIndex != currentChunkIndex || currentChunk == null)
            {
                currentChunk = await chunkManager.GetChunkAsync(targetChunkIndex, ct);
                currentChunkIndex = targetChunkIndex;

                chunkManager.PreloadAdjacentChunksAsync(targetChunkIndex, ct, false);
            }

            if (currentChunk?.Keyframe != null)
            {
                ApplyKeyframe(currentChunk.Keyframe);
            }

            foreach (var frame in currentChunk.Frames)
            {
                if (frame.Time > time)
                {
                    break;
                }

                ApplyFrameDeltas(frame);
                currentFrameIndex = frame.Index;
            }

            ApplyCurrentStateToObjects();
            OnTimeChanged?.Invoke(currentTime);
        }

        /// <summary>진행률로 이동</summary>
        public void SeekToProgress(float progress)
        {
            SeekToTime(progress * Duration);
        }

        private void SetState(PlaybackState state)
        {
            if (currentState == state)
            {
                return;
            }

            currentState = state;
            OnStateChanged?.Invoke(state);
        }

        private async UniTaskVoid PlaybackLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (currentState == PlaybackState.Paused)
                {
                    await UniTask.Yield(ct);
                    continue;
                }

                float direction = currentState == PlaybackState.Reversing ? -1f : 1f;
                float deltaTime = Time.deltaTime * playbackSpeed * direction;
                float newTime = currentTime + deltaTime;

                if (newTime >= Duration)
                {
                    currentTime = Duration;
                    ApplyCurrentStateToObjects();
                    SetState(PlaybackState.Stopped);
                    OnPlaybackComplete?.Invoke();
                    break;
                }

                if (newTime <= 0f)
                {
                    currentTime = 0f;
                    await ApplyInitialStateAsync(ct);
                    SetState(PlaybackState.Stopped);
                    OnPlaybackComplete?.Invoke();
                    break;
                }

                await UpdatePlaybackAsync(newTime, direction > 0, ct);
                currentTime = newTime;
                OnTimeChanged?.Invoke(currentTime);

                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            }
        }

        private async UniTask UpdatePlaybackAsync(float targetTime, bool forward, CancellationToken ct)
        {
            int targetChunkIndex = FindChunkIndexForTime(targetTime);

            if (targetChunkIndex != currentChunkIndex)
            {
                int previousChunkIndex = currentChunkIndex;

                currentChunk = await chunkManager.GetChunkAsync(targetChunkIndex, ct);
                currentChunkIndex = targetChunkIndex;

                // 청크 변경 시 캐시 관리
                HandleChunkTransitionCache(previousChunkIndex, targetChunkIndex, forward);

                // 청크 변경 시 이벤트 추적 리셋
                ResetEventTrackingForChunk();

                if (currentChunk?.Keyframe != null)
                {
                    ApplyKeyframe(currentChunk.Keyframe);

                    // 키프레임 시간을 이벤트 처리 시작점으로 설정
                    lastEventProcessTime = currentChunk.Keyframe.Time;
                }

                // 재생 방향에 따른 스마트 프리로딩
                bool isReversing = currentState == PlaybackState.Reversing;
                chunkManager.PreloadAdjacentChunksAsync(targetChunkIndex, ct, isReversing);
            }

            if (currentChunk == null)
            {
                return;
            }

            if (forward)
            {
                ProcessFramesForward(targetTime);
            }
            else
            {
                ProcessFramesReverse(targetTime);
            }

            ApplyInterpolatedState(targetTime);
        }

        private void ProcessFramesForward(float targetTime)
        {
            foreach (var frame in currentChunk.Frames)
            {
                if (frame.Time > targetTime)
                {
                    break;
                }

                if (frame.Index <= currentFrameIndex)
                {
                    continue;
                }

                // 1단계: PreState 이벤트 처리 (Instantiate 등 - 오브젝트 생성)
                ProcessEventsInFrameByPhase(frame, EventProcessingPhase.PreState);

                // 2단계: 상태 델타 적용 (Transform, Rigidbody, Animator)
                ApplyFrameDeltas(frame);

                // 3단계: PostState 이벤트 처리 (Animation, Audio 등)
                ProcessEventsInFrameByPhase(frame, EventProcessingPhase.PostState);

                // 이벤트 처리 완료 후 시간 업데이트
                lastEventProcessTime = frame.Time;
                currentFrameIndex = frame.Index;
            }
        }

        private void ProcessFramesReverse(float targetTime)
        {
            // 역방향 이벤트 처리 (lastEventProcessTime > targetTime)
            ProcessEventsInTimeRangeReverse(lastEventProcessTime, targetTime);
            lastEventProcessTime = targetTime;

            if (currentChunk?.Keyframe != null && currentChunk.Keyframe.Time <= targetTime)
            {
                ApplyKeyframe(currentChunk.Keyframe);

                // 역재생 시 이벤트 실행 인덱스 재계산
                RebuildExecutedEventIndices(targetTime);

                foreach (var frame in currentChunk.Frames)
                {
                    if (frame.Time > targetTime)
                    {
                        break;
                    }

                    ApplyFrameDeltas(frame);
                }
            }
        }

        /// <summary>특정 시간까지 실행된 이벤트 인덱스 재계산 (역재생 후 키프레임 적용 시)</summary>
        private void RebuildExecutedEventIndices(float upToTime)
        {
            executedEventIndices.Clear();

            if (currentChunk?.Events == null)
            {
                return;
            }

            for (int i = 0; i < currentChunk.Events.Count; i++)
            {
                if (currentChunk.Events[i].Time <= upToTime)
                {
                    executedEventIndices.Add(i);
                }
            }
        }

        private void ApplyInitialState()
        {
            ApplyInitialStateAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask ApplyInitialStateAsync(CancellationToken ct)
        {
            if (loadedReplay?.InitialState == null)
            {
                return;
            }

            reconstructedStates.Clear();
            previousStates.Clear();

            foreach (var objState in loadedReplay.InitialState.Objects)
            {
                var state = new CompressedObjectState
                {
                    ObjectId = objState.Id,
                    Transform = objState.Transform,
                    HasRigidbody = objState.HasRigidbody,
                    Rigidbody = objState.Rigidbody,
                    HasAnimator = objState.HasAnimator,
                    Animator = objState.Animator
                };
                reconstructedStates[objState.Id] = state;
                previousStates[objState.Id] = state;
            }

            currentChunk = await chunkManager.GetChunkAsync(0, ct);
            currentChunkIndex = 0;
            currentFrameIndex = 0;

            ApplyCurrentStateToObjects();
        }

        private void ApplyKeyframe(ReplayKeyframe keyframe)
        {
            foreach (var state in keyframe.States)
            {
                reconstructedStates[state.ObjectId] = state;
            }
        }

        private void ApplyFrameDeltas(ReplayFrame frame)
        {
            if (frame.TransformDeltas != null)
            {
                foreach (var delta in frame.TransformDeltas)
                {
                    if (reconstructedStates.TryGetValue(delta.ObjectId, out var state))
                    {
                        // 보간을 위해 이전 상태 저장
                        previousStates[delta.ObjectId] = state;

                        state.Transform = delta.Transform;
                        reconstructedStates[delta.ObjectId] = state;
                    }
                }
            }

            if (frame.RigidbodyDeltas != null)
            {
                foreach (var delta in frame.RigidbodyDeltas)
                {
                    if (reconstructedStates.TryGetValue(delta.ObjectId, out var state))
                    {
                        state.Rigidbody = delta.Rigidbody;
                        reconstructedStates[delta.ObjectId] = state;
                    }
                }
            }

            // Animator 델타 적용
            if (frame.AnimatorDeltas != null)
            {
                foreach (var delta in frame.AnimatorDeltas)
                {
                    if (reconstructedStates.TryGetValue(delta.ObjectId, out var state))
                    {
                        // 보간을 위해 이전 상태 저장
                        previousStates[delta.ObjectId] = state;

                        state.HasAnimator = true;
                        state.Animator = delta.State;
                        reconstructedStates[delta.ObjectId] = state;
                    }
                }
            }
        }

        private void ProcessEventsInFrame(ReplayFrame frame)
        {
            ProcessEventsInTimeRange(lastEventProcessTime, frame.Time, null);
            lastEventProcessTime = frame.Time;
        }

        /// <summary>프레임 내 특정 단계의 이벤트만 처리</summary>
        private void ProcessEventsInFrameByPhase(ReplayFrame frame, EventProcessingPhase phase)
        {
            ProcessEventsInTimeRange(lastEventProcessTime, frame.Time, phase);
            // Note: lastEventProcessTime은 ProcessFramesForward 마지막에 한 번만 업데이트
        }

        /// <summary>시간 범위 내 이벤트 처리 (정방향)</summary>
        /// <param name="phase">null이면 모든 이벤트, 지정하면 해당 단계만</param>
        private void ProcessEventsInTimeRange(float fromTime, float toTime, EventProcessingPhase? phase)
        {
            if (currentChunk?.Events == null || currentChunk.Events.Count == 0)
            {
                return;
            }

            for (int i = 0; i < currentChunk.Events.Count; i++)
            {
                var evt = currentChunk.Events[i];

                // 이미 실행된 이벤트 스킵
                if (executedEventIndices.Contains(i))
                {
                    continue;
                }

                // 단계 필터링 (지정된 경우)
                if (phase.HasValue && evt.EventType.GetProcessingPhase() != phase.Value)
                {
                    continue;
                }

                // 시간 범위 체크 (fromTime < evt.Time <= toTime)
                if (evt.Time > fromTime && evt.Time <= toTime)
                {
                    ExecuteEventWithIndex(evt, i);
                    executedEventIndices.Add(i);
                }
            }
        }

        /// <summary>시간 범위 내 이벤트 처리 (역방향)</summary>
        private void ProcessEventsInTimeRangeReverse(float fromTime, float toTime)
        {
            if (currentChunk?.Events == null || currentChunk.Events.Count == 0)
            {
                return;
            }

            // 역방향: fromTime > toTime
            for (int i = currentChunk.Events.Count - 1; i >= 0; i--)
            {
                var evt = currentChunk.Events[i];

                // 시간 범위 체크 (toTime <= evt.Time < fromTime)
                if (evt.Time >= toTime && evt.Time < fromTime)
                {
                    // 역재생 가능한 이벤트만 처리
                    ExecuteEventReverse(evt);
                    executedEventIndices.Remove(i);
                }
            }
        }

        /// <summary>청크 변경 시 이벤트 인덱스 초기화</summary>
        private void ResetEventTrackingForChunk()
        {
            executedEventIndices.Clear();
        }

        /// <summary>청크 전환 시 캐시 관리</summary>
        private void HandleChunkTransitionCache(int fromChunkIndex, int toChunkIndex, bool forward)
        {
            if (forward)
            {
                // 정방향 이동: 복원된 오브젝트가 다시 파괴될 수 있으므로 캐시 유지
                // 새 청크에서 Destroy 이벤트가 발생하면 다시 캐싱됨
                if (showDebugInfo && destroyedObjectsCache.Count > 0)
                {
                    Debug.Log($"[ReplayPlayer] Forward chunk transition: keeping {destroyedObjectsCache.Count} cached objects");
                }
            }
            else
            {
                // 역방향 이동: 이전 청크로 이동 시, 해당 청크의 Destroy 이벤트에 대한 캐시가 필요
                // 현재 청크에서 파괴된 오브젝트들을 미리 캐싱해야 함
                PreCacheDestroyEventsForChunk(currentChunk);

                if (showDebugInfo && destroyedObjectsCache.Count > 0)
                {
                    Debug.Log($"[ReplayPlayer] Reverse chunk transition: {destroyedObjectsCache.Count} objects in cache");
                }
            }
        }

        /// <summary>청크 내 Destroy 이벤트에 대한 오브젝트를 미리 캐싱 (역재생 준비)</summary>
        private void PreCacheDestroyEventsForChunk(ReplayChunk chunk)
        {
            if (chunk?.Events == null || chunk.Events.Count == 0)
            {
                return;
            }

            for (int i = 0; i < chunk.Events.Count; i++)
            {
                var evt = chunk.Events[i];

                if (evt.EventType == ReplayEventType.Destroy)
                {
                    // 이미 캐시에 있으면 스킵
                    if (destroyedObjectsCache.ContainsKey(evt.TargetObjectId))
                    {
                        continue;
                    }

                    // 현재 reconstructedStates에서 상태를 가져와 캐싱
                    CacheDestroyedObject(evt.TargetObjectId, evt.Time, i);
                }
            }
        }

        private void ExecuteEvent(ReplayEvent evt)
        {
            ExecuteEventWithIndex(evt, -1);
        }

        private void ExecuteEventWithIndex(ReplayEvent evt, int eventIndex)
        {
            switch (evt.EventType)
            {
                case ReplayEventType.Instantiate:
                    HandleInstantiateEvent(evt);
                    break;

                case ReplayEventType.Destroy:
                    HandleDestroyEventInternal(evt, eventIndex);
                    break;

                case ReplayEventType.NetworkRpc:
                case ReplayEventType.NetworkVarChange:
                case ReplayEventType.OwnershipChange:
                case ReplayEventType.NetworkSpawn:
                case ReplayEventType.NetworkDespawn:
                case ReplayEventType.SyncListChange:
                case ReplayEventType.SyncDictionaryChange:
                    if (networkSync != null && networkHandler != null)
                    {
                        networkSync.ExecuteNetworkEvent(evt, networkHandler);
                    }
                    else if (showDebugInfo)
                    {
                        Debug.LogWarning($"[ReplayPlayer] Network handler not set for event: {evt.EventType}");
                    }
                    break;

                case ReplayEventType.MethodCall:
                case ReplayEventType.Animation:
                case ReplayEventType.Audio:
                case ReplayEventType.Particle:
                case ReplayEventType.Custom:
                    if (!EventCapture.Instance.ExecuteEvent(evt) && showDebugInfo)
                    {
                        Debug.LogWarning($"[ReplayPlayer] No handler registered for event: {evt.EventName}");
                    }
                    break;

                default:
                    if (showDebugInfo)
                    {
                        Debug.LogWarning($"[ReplayPlayer] Unknown event type: {evt.EventType}");
                    }
                    break;
            }
        }

        /// <summary>역재생 시 이벤트 처리 (역방향 가능한 이벤트만)</summary>
        private void ExecuteEventReverse(ReplayEvent evt)
        {
            switch (evt.EventType)
            {
                case ReplayEventType.Instantiate:
                    // 역재생: 생성 → 파괴
                    HandleDestroyEvent(evt);
                    break;

                case ReplayEventType.Destroy:
                    // 역재생: 파괴 → 재생성 (캐시에서 복원)
                    RestoreDestroyedObject(evt.TargetObjectId);
                    break;

                // 다른 이벤트 타입은 역재생 시 스킵 (상태 기반이 아니므로)
                default:
                    break;
            }
        }

        /// <summary>파괴된 오브젝트를 캐시에서 복원합니다 (역재생용).</summary>
        private void RestoreDestroyedObject(int objectId)
        {
            if (!destroyedObjectsCache.TryGetValue(objectId, out var cache))
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[ReplayPlayer] Cannot restore object {objectId}: not found in cache");
                }
                return;
            }

            if (dynamicObjectTracker == null)
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[ReplayPlayer] Cannot restore object {objectId}: DynamicObjectTracker is null");
                }
                return;
            }

            // 캐시된 위치/회전으로 오브젝트 재생성
            Vector3 position = cache.LastTransform.GetPosition();
            Quaternion rotation = cache.LastTransform.GetRotation();

            var obj = dynamicObjectTracker.RestoreFromCache(
                objectId,
                cache.PrefabName,
                position,
                rotation,
                cache.HasRigidbody);

            if (obj != null)
            {
                // Transform 캡처 등록
                var capture = obj.GetComponent<TransformCapture>();

                if (capture != null)
                {
                    transformMap[objectId] = capture;
                }

                // Rigidbody 캡처 등록
                var rbCapture = obj.GetComponent<RigidbodyCapture>();

                if (rbCapture != null)
                {
                    rigidbodyMap[objectId] = rbCapture;
                }

                // Animator 캡처 등록
                var animCapture = obj.GetComponent<AnimatorCapture>();

                if (animCapture != null)
                {
                    animatorMap[objectId] = animCapture;
                }

                // 상태 복원
                var state = new CompressedObjectState
                {
                    ObjectId = objectId,
                    Transform = cache.LastTransform,
                    HasRigidbody = cache.HasRigidbody,
                    Rigidbody = cache.LastRigidbody,
                    HasAnimator = cache.HasAnimator,
                    Animator = cache.LastAnimator
                };

                reconstructedStates[objectId] = state;
                previousStates[objectId] = state;

                // 캐시에서 제거
                destroyedObjectsCache.Remove(objectId);

                if (showDebugInfo)
                {
                    Debug.Log($"[ReplayPlayer] Restored object: {objectId} ({cache.PrefabName})");
                }
            }
        }

        private void HandleInstantiateEvent(ReplayEvent evt)
        {
            if (dynamicObjectTracker == null)
            {
                return;
            }

            var spawnData = DeserializeSpawnData(evt.Parameters);
            string prefabName = evt.EventName;

            var obj = dynamicObjectTracker.RecreateObject(
                evt.TargetObjectId,
                prefabName,
                spawnData.position,
                spawnData.rotation);

            if (obj != null)
            {
                var capture = obj.GetComponent<TransformCapture>();

                if (capture != null)
                {
                    transformMap[evt.TargetObjectId] = capture;
                }

                var rbCapture = obj.GetComponent<RigidbodyCapture>();

                if (rbCapture != null)
                {
                    rigidbodyMap[evt.TargetObjectId] = rbCapture;
                }

                var animCapture = obj.GetComponent<AnimatorCapture>();

                if (animCapture != null)
                {
                    animatorMap[evt.TargetObjectId] = animCapture;
                }
            }
        }

        private void HandleDestroyEvent(ReplayEvent evt)
        {
            HandleDestroyEventInternal(evt, -1);
        }

        /// <summary>Destroy 이벤트 처리 (내부용, 이벤트 인덱스 포함)</summary>
        private void HandleDestroyEventInternal(ReplayEvent evt, int eventIndex)
        {
            int objectId = evt.TargetObjectId;

            // 역재생 복원을 위해 파괴 전 상태 캐싱
            CacheDestroyedObject(objectId, evt.Time, eventIndex);

            if (dynamicObjectTracker != null)
            {
                dynamicObjectTracker.DestroyTrackedObject(objectId);
            }

            transformMap.Remove(objectId);
            rigidbodyMap.Remove(objectId);
            animatorMap.Remove(objectId);
            reconstructedStates.Remove(objectId);
            previousStates.Remove(objectId);
        }

        /// <summary>파괴된 오브젝트 상태를 캐시에 저장 (역재생 복원용)</summary>
        private void CacheDestroyedObject(int objectId, float destroyTime, int eventIndex)
        {
            // 이미 캐시에 있으면 스킵 (중복 방지)
            if (destroyedObjectsCache.ContainsKey(objectId))
            {
                return;
            }

            // InitialState에서 프리팹 이름 조회
            string prefabName = null;
            bool hasRigidbody = false;

            if (loadedReplay?.InitialState?.Objects != null)
            {
                foreach (var objState in loadedReplay.InitialState.Objects)
                {
                    if (objState.Id == objectId)
                    {
                        prefabName = objState.PrefabName;
                        hasRigidbody = objState.HasRigidbody;
                        break;
                    }
                }
            }

            // 현재 상태에서 Transform/Rigidbody/Animator 정보 가져오기
            CompressedTransform lastTransform = default;
            CompressedRigidbody lastRigidbody = default;
            CompressedAnimatorState lastAnimator = default;
            bool hasAnimator = false;

            if (reconstructedStates.TryGetValue(objectId, out var state))
            {
                lastTransform = state.Transform;
                lastRigidbody = state.Rigidbody;
                hasRigidbody = state.HasRigidbody;
                lastAnimator = state.Animator;
                hasAnimator = state.HasAnimator;
            }

            // prefabName이 없으면 DynamicObjectTracker에서 조회
            if (string.IsNullOrEmpty(prefabName) && dynamicObjectTracker != null)
            {
                var tracked = dynamicObjectTracker.GetTrackedObject(objectId);

                if (tracked != null)
                {
                    prefabName = tracked.PrefabName;
                }
            }

            if (string.IsNullOrEmpty(prefabName))
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[ReplayPlayer] Cannot cache destroyed object {objectId}: prefab name unknown");
                }
                return;
            }

            destroyedObjectsCache[objectId] = new DestroyedObjectCache
            {
                ObjectId = objectId,
                PrefabName = prefabName,
                LastTransform = lastTransform,
                LastRigidbody = lastRigidbody,
                HasRigidbody = hasRigidbody,
                LastAnimator = lastAnimator,
                HasAnimator = hasAnimator,
                DestroyTime = destroyTime,
                EventIndex = eventIndex
            };

            if (showDebugInfo)
            {
                Debug.Log($"[ReplayPlayer] Cached destroyed object: {objectId} ({prefabName}) at time {destroyTime:F2}");
            }
        }

        private (Vector3 position, Quaternion rotation) DeserializeSpawnData(byte[] data)
        {
            if (data == null || data.Length < 28)
            {
                return (Vector3.zero, Quaternion.identity);
            }

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                var position = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                var rotation = new Quaternion(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                return (position, rotation);
            }
        }

        private void ApplyInterpolatedState(float time)
        {
            // 보간 중에도 이벤트 처리 (시간 기반)
            ProcessEventsInTimeRange(lastEventProcessTime, time, EventProcessingPhase.PreState);
            lastEventProcessTime = time;

            if (!interpolateTransforms)
            {
                ApplyCurrentStateToObjects();
                return;
            }

            // 현재 프레임과 다음 프레임 사이의 보간 시간 계산
            float frameTime = 0f;
            float nextFrameTime = 0f;

            if (currentChunk != null && currentChunk.Frames.Count > 0)
            {
                // 현재 시간에 해당하는 프레임 찾기
                // Note: ReplayFrame은 struct이므로 default 초기화 시 List 필드가 null임
                // foundCurrentFrame 플래그로 유효한 프레임 할당 여부 추적
                ReplayFrame currentFrame = default;
                ReplayFrame nextFrame = default;
                bool foundCurrentFrame = false;
                bool foundNextFrame = false;

                for (int i = 0; i < currentChunk.Frames.Count; i++)
                {
                    var frame = currentChunk.Frames[i];

                    if (frame.Time <= time)
                    {
                        currentFrame = frame;
                        frameTime = frame.Time;
                        foundCurrentFrame = true;

                        if (i + 1 < currentChunk.Frames.Count)
                        {
                            nextFrame = currentChunk.Frames[i + 1];
                            nextFrameTime = nextFrame.Time;
                            foundNextFrame = true;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // 유효한 프레임을 찾지 못한 경우 현재 상태 적용
                if (!foundCurrentFrame)
                {
                    ApplyCurrentStateToObjects();
                    return;
                }

                // 보간 비율 계산
                float t = 0f;

                if (foundNextFrame && nextFrameTime > frameTime)
                {
                    t = Mathf.Clamp01((time - frameTime) / (nextFrameTime - frameTime));
                }

                // 각 오브젝트에 보간 적용
                foreach (var kvp in reconstructedStates)
                {
                    int objectId = kvp.Key;
                    var currentState = kvp.Value;

                    if (!transformMap.TryGetValue(objectId, out var capture) || capture == null)
                    {
                        continue;
                    }

                    // 이전 상태가 있으면 보간, 없으면 현재 상태 적용
                    if (previousStates.TryGetValue(objectId, out var prevState) && t > 0f)
                    {
                        ApplyInterpolatedTransform(capture.transform, prevState.Transform, currentState.Transform, t);
                    }
                    else
                    {
                        currentState.Transform.ApplyToTransform(capture.transform);
                    }

                    // 물리 상태 적용
                    if (interpolatePhysics && currentState.HasRigidbody)
                    {
                        if (rigidbodyMap.TryGetValue(objectId, out var rbCapture) && rbCapture != null)
                        {
                            rbCapture.ApplyState(currentState.Rigidbody);
                        }
                    }

                    // Animator 상태 적용 (보간 포함, 역재생 시 특수 처리)
                    if (currentState.HasAnimator)
                    {
                        if (animatorMap.TryGetValue(objectId, out var animCapture) && animCapture != null)
                        {
                            bool isReversing = this.currentState == PlaybackState.Reversing;

                            if (interpolateAnimators && previousStates.TryGetValue(objectId, out var prevAnimState) && t > 0f && prevAnimState.HasAnimator)
                            {
                                animCapture.ApplyInterpolatedState(prevAnimState.Animator, currentState.Animator, t);
                            }
                            else if (isReversing)
                            {
                                animCapture.ApplyStateForReverse(currentState.Animator);
                            }
                            else
                            {
                                animCapture.ApplyState(currentState.Animator);
                            }
                        }
                    }
                }
            }
            else
            {
                ApplyCurrentStateToObjects();
            }
        }

        private void ApplyInterpolatedTransform(Transform target, CompressedTransform from, CompressedTransform to, float t)
        {
            // 위치 보간
            Vector3 fromPos = from.GetPosition();
            Vector3 toPos = to.GetPosition();
            target.position = Vector3.Lerp(fromPos, toPos, t);

            // 회전 보간 (Slerp)
            Quaternion fromRot = from.GetRotation();
            Quaternion toRot = to.GetRotation();
            target.rotation = Quaternion.Slerp(fromRot, toRot, t);

            // 스케일 보간
            Vector3 fromScale = from.GetScale();
            Vector3 toScale = to.GetScale();
            target.localScale = Vector3.Lerp(fromScale, toScale, t);

            // 활성화 상태는 현재 프레임 기준
            target.gameObject.SetActive(to.IsActive);
        }

        private void ApplyCurrentStateToObjects()
        {
            bool isReversing = currentState == PlaybackState.Reversing;

            foreach (var kvp in reconstructedStates)
            {
                if (transformMap.TryGetValue(kvp.Key, out var capture) && capture != null)
                {
                    kvp.Value.Transform.ApplyToTransform(capture.transform);
                }

                if (interpolatePhysics && kvp.Value.HasRigidbody)
                {
                    if (rigidbodyMap.TryGetValue(kvp.Key, out var rbCapture) && rbCapture != null)
                    {
                        rbCapture.ApplyState(kvp.Value.Rigidbody);
                    }
                }

                // Animator 상태 적용 (역재생 시 특수 처리)
                if (kvp.Value.HasAnimator)
                {
                    if (animatorMap.TryGetValue(kvp.Key, out var animCapture) && animCapture != null)
                    {
                        if (isReversing)
                        {
                            animCapture.ApplyStateForReverse(kvp.Value.Animator);
                        }
                        else
                        {
                            animCapture.ApplyState(kvp.Value.Animator);
                        }
                    }
                }
            }
        }

        private int FindChunkIndexForTime(float time)
        {
            var headers = loadedReplay?.ChunkHeaders;

            if (headers == null || headers.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < headers.Count; i++)
            {
                if (time >= headers[i].StartTime && time <= headers[i].EndTime)
                {
                    return i;
                }
            }

            return time <= 0 ? 0 : headers.Count - 1;
        }

        private void OnGUI()
        {
            if (!showDebugInfo || loadedReplay == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 350, 180));
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Time: {currentTime:F2}s / {Duration:F2}s ({Progress * 100:F1}%)");
            GUILayout.Label($"Speed: {playbackSpeed:F1}x");
            GUILayout.Label($"Frame: {currentFrameIndex}");
            GUILayout.Label($"Chunk: {currentChunkIndex}/{loadedReplay.Metadata.ChunkCount}");
            GUILayout.Label($"Cached Chunks: {chunkManager?.CachedChunkCount ?? 0}");
            GUILayout.Label($"Objects: {transformMap.Count} T / {rigidbodyMap.Count} RB / {animatorMap.Count} Anim");
            GUILayout.Label($"Destroyed Cache: {destroyedObjectsCache.Count}");
            GUILayout.EndArea();
        }
    }

    /// <summary>파괴된 오브젝트 캐시 (역재생 복원용)</summary>
    public struct DestroyedObjectCache
    {
        /// <summary>오브젝트 ID</summary>
        public int ObjectId;

        /// <summary>프리팹 이름</summary>
        public string PrefabName;

        /// <summary>파괴 시점의 Transform 상태</summary>
        public CompressedTransform LastTransform;

        /// <summary>파괴 시점의 Rigidbody 상태</summary>
        public CompressedRigidbody LastRigidbody;

        /// <summary>Rigidbody 존재 여부</summary>
        public bool HasRigidbody;

        /// <summary>파괴 시점의 Animator 상태</summary>
        public CompressedAnimatorState LastAnimator;

        /// <summary>Animator 존재 여부</summary>
        public bool HasAnimator;

        /// <summary>파괴된 시간</summary>
        public float DestroyTime;

        /// <summary>파괴 이벤트 인덱스 (청크 내)</summary>
        public int EventIndex;
    }
}
