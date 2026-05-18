using System;
using System.Collections.Generic;
using System.IO;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Capture
{
    /// <summary>이벤트 캡처 및 재생 시스템입니다.</summary>
    public class EventCapture : MonoBehaviour
    {
        private static EventCapture instance;
        private static readonly List<ReplayEvent> EmptyEventList = new List<ReplayEvent>(0);

        // 더블 버퍼링으로 할당 최소화
        private List<ReplayEvent> pendingEvents = new List<ReplayEvent>();
        private List<ReplayEvent> swapBuffer = new List<ReplayEvent>();
        private readonly Dictionary<string, Action<int, byte[]>> eventHandlers = new Dictionary<string, Action<int, byte[]>>();

        // AnimatorCapture와 충돌 방지용 (state-based 캡처 중인 오브젝트 추적)
        private readonly HashSet<int> objectsWithAnimatorCapture = new HashSet<int>();

        private float currentTime;
        private int currentFrame;
        private bool isRecording;

        /// <summary>Animation 이벤트 중복 기록 시 경고 출력 여부</summary>
        public bool WarnOnAnimatorCaptureConflict { get; set; } = true;

        /// <summary>싱글톤 인스턴스</summary>
        public static EventCapture Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[EventCapture]");
                    instance = go.AddComponent<EventCapture>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>녹화 상태 설정</summary>
        public void SetRecordingState(bool recording, float time, int frame)
        {
            isRecording = recording;
            currentTime = time;
            currentFrame = frame;

            if (!recording)
            {
                pendingEvents.Clear();
            }
        }

        /// <summary>시간/프레임 업데이트</summary>
        public void UpdateTime(float time, int frame)
        {
            currentTime = time;
            currentFrame = frame;
        }

        /// <summary>대기 중인 이벤트 가져오고 초기화 (버퍼 스왑으로 할당 최소화)</summary>
        public List<ReplayEvent> FlushEvents()
        {
            // 이벤트가 없으면 할당 없이 빈 결과 반환
            if (pendingEvents.Count == 0)
            {
                return EmptyEventList;
            }

            // 버퍼 스왑: pendingEvents를 반환하고, swapBuffer를 새 pendingEvents로 사용
            // 호출자는 AddRange로 데이터를 복사하므로, 다음 프레임에 Clear해도 안전
            var result = pendingEvents;
            pendingEvents = swapBuffer;
            pendingEvents.Clear();
            swapBuffer = result;

            return result;
        }

        /// <summary>이벤트 핸들러 등록</summary>
        public void RegisterHandler(string eventName, Action<int, byte[]> handler)
        {
            eventHandlers[eventName] = handler;
        }

        /// <summary>이벤트 핸들러 해제</summary>
        public void UnregisterHandler(string eventName)
        {
            eventHandlers.Remove(eventName);
        }

        /// <summary>AnimatorCapture가 있는 오브젝트 등록 (충돌 방지용)</summary>
        public void RegisterAnimatorCaptureObject(int objectId)
        {
            objectsWithAnimatorCapture.Add(objectId);
        }

        /// <summary>AnimatorCapture가 있는 오브젝트 해제</summary>
        public void UnregisterAnimatorCaptureObject(int objectId)
        {
            objectsWithAnimatorCapture.Remove(objectId);
        }

        /// <summary>모든 AnimatorCapture 오브젝트 등록 해제</summary>
        public void ClearAnimatorCaptureObjects()
        {
            objectsWithAnimatorCapture.Clear();
        }

        /// <summary>메서드 호출 이벤트 기록</summary>
        public void RecordMethodCall(int objectId, string methodName, params object[] parameters)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.MethodCall,
                EventName = methodName,
                Parameters = SerializeParameters(parameters)
            };

            pendingEvents.Add(evt);
        }

        /// <summary>오브젝트 생성 이벤트 기록</summary>
        public void RecordInstantiate(int objectId, string prefabName, Vector3 position, Quaternion rotation)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Instantiate,
                EventName = prefabName,
                Parameters = SerializeParameters(position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w)
            };

            pendingEvents.Add(evt);
        }

        /// <summary>오브젝트 파괴 이벤트 기록</summary>
        public void RecordDestroy(int objectId)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Destroy,
                EventName = string.Empty,
                Parameters = null
            };

            pendingEvents.Add(evt);
        }

        /// <summary>애니메이션 이벤트 기록</summary>
        /// <remarks>
        /// AnimatorCapture가 있는 오브젝트에 대해서는 상태 기반 캡처가 우선됩니다.
        /// 이벤트 기반 기록을 강제하려면 forceRecord=true를 사용하세요.
        /// </remarks>
        public void RecordAnimation(int objectId, string triggerName, int stateHash = 0, bool forceRecord = false)
        {
            if (!isRecording)
            {
                return;
            }

            // AnimatorCapture 충돌 체크 (state-based 캡처가 우선)
            if (!forceRecord && objectsWithAnimatorCapture.Contains(objectId))
            {
                if (WarnOnAnimatorCaptureConflict)
                {
                    Debug.LogWarning($"[EventCapture] Animation event for object {objectId} skipped - AnimatorCapture is active. " +
                                   $"State-based capture takes precedence. Use forceRecord=true to override.");
                }
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Animation,
                EventName = triggerName,
                Parameters = SerializeParameters(stateHash)
            };

            pendingEvents.Add(evt);
        }

        /// <summary>오디오 이벤트 기록</summary>
        public void RecordAudio(int objectId, string clipName, float volume = 1f, bool loop = false)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Audio,
                EventName = clipName,
                Parameters = SerializeParameters(volume, loop)
            };

            pendingEvents.Add(evt);
        }

        /// <summary>파티클 이벤트 기록</summary>
        public void RecordParticle(int objectId, string particleName, bool play = true)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Particle,
                EventName = particleName,
                Parameters = SerializeParameters(play)
            };

            pendingEvents.Add(evt);
        }

        /// <summary>커스텀 이벤트 기록</summary>
        public void RecordCustomEvent(int objectId, string eventName, byte[] data)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new ReplayEvent
            {
                Time = currentTime,
                FrameIndex = currentFrame,
                TargetObjectId = objectId,
                EventType = ReplayEventType.Custom,
                EventName = eventName,
                Parameters = data
            };

            pendingEvents.Add(evt);
        }

        /// <summary>이벤트 실행 (재생 시)</summary>
        /// <returns>핸들러가 존재하고 실행되었으면 true</returns>
        public bool ExecuteEvent(ReplayEvent evt)
        {
            if (eventHandlers.TryGetValue(evt.EventName, out var handler))
            {
                handler?.Invoke(evt.TargetObjectId, evt.Parameters);
                return true;
            }

            return false;
        }

        private byte[] SerializeParameters(params object[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return null;
            }

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(parameters.Length);

                foreach (var param in parameters)
                {
                    WriteParameter(writer, param);
                }

                return ms.ToArray();
            }
        }

        private void WriteParameter(BinaryWriter writer, object param)
        {
            switch (param)
            {
                case null:
                    writer.Write((byte)0);
                    break;
                case int i:
                    writer.Write((byte)1);
                    writer.Write(i);
                    break;
                case float f:
                    writer.Write((byte)2);
                    writer.Write(f);
                    break;
                case bool b:
                    writer.Write((byte)3);
                    writer.Write(b);
                    break;
                case string s:
                    writer.Write((byte)4);
                    writer.Write(s);
                    break;
                case Vector3 v:
                    writer.Write((byte)5);
                    writer.Write(v.x);
                    writer.Write(v.y);
                    writer.Write(v.z);
                    break;
                case Quaternion q:
                    writer.Write((byte)6);
                    writer.Write(q.x);
                    writer.Write(q.y);
                    writer.Write(q.z);
                    writer.Write(q.w);
                    break;
                case byte[] bytes:
                    writer.Write((byte)7);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    break;
                default:
                    writer.Write((byte)0);
                    break;
            }
        }

        /// <summary>파라미터 역직렬화</summary>
        public static object[] DeserializeParameters(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<object>();
            }

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                var result = new object[count];

                for (int i = 0; i < count; i++)
                {
                    result[i] = ReadParameter(reader);
                }

                return result;
            }
        }

        private static object ReadParameter(BinaryReader reader)
        {
            byte type = reader.ReadByte();

            return type switch
            {
                1 => reader.ReadInt32(),
                2 => reader.ReadSingle(),
                3 => reader.ReadBoolean(),
                4 => reader.ReadString(),
                5 => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                6 => new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                7 => reader.ReadBytes(reader.ReadInt32()),
                _ => null
            };
        }
    }
}
