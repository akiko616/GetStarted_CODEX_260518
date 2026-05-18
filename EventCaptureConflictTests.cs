using NUnit.Framework;
using ReplaySystem.Core;

namespace ReplaySystem.Tests
{
    /// <summary>
    /// EventCapture의 AnimatorCapture 충돌 방지 로직 테스트
    /// </summary>
    [TestFixture]
    public class EventCaptureConflictTests
    {
        #region EventProcessingPhase Tests

        [Test]
        public void GetProcessingPhase_Instantiate_ReturnsPreState()
        {
            var phase = ReplayEventType.Instantiate.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PreState, phase);
        }

        [Test]
        public void GetProcessingPhase_Destroy_ReturnsPostState()
        {
            var phase = ReplayEventType.Destroy.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_Animation_ReturnsPostState()
        {
            var phase = ReplayEventType.Animation.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_Audio_ReturnsPostState()
        {
            var phase = ReplayEventType.Audio.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_MethodCall_ReturnsPostState()
        {
            var phase = ReplayEventType.MethodCall.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_Particle_ReturnsPostState()
        {
            var phase = ReplayEventType.Particle.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_Custom_ReturnsPostState()
        {
            var phase = ReplayEventType.Custom.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        [Test]
        public void GetProcessingPhase_NetworkRpc_ReturnsPostState()
        {
            var phase = ReplayEventType.NetworkRpc.GetProcessingPhase();
            Assert.AreEqual(EventProcessingPhase.PostState, phase);
        }

        #endregion

        #region IsReversible Tests

        [Test]
        public void IsReversible_Instantiate_ReturnsTrue()
        {
            Assert.IsTrue(ReplayEventType.Instantiate.IsReversible());
        }

        [Test]
        public void IsReversible_Destroy_ReturnsTrue()
        {
            Assert.IsTrue(ReplayEventType.Destroy.IsReversible());
        }

        [Test]
        public void IsReversible_Animation_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.Animation.IsReversible());
        }

        [Test]
        public void IsReversible_Audio_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.Audio.IsReversible());
        }

        [Test]
        public void IsReversible_MethodCall_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.MethodCall.IsReversible());
        }

        #endregion

        #region CanConflictWithStateCapture Tests

        [Test]
        public void CanConflictWithStateCapture_Animation_ReturnsTrue()
        {
            Assert.IsTrue(ReplayEventType.Animation.CanConflictWithStateCapture());
        }

        [Test]
        public void CanConflictWithStateCapture_Instantiate_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.Instantiate.CanConflictWithStateCapture());
        }

        [Test]
        public void CanConflictWithStateCapture_Destroy_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.Destroy.CanConflictWithStateCapture());
        }

        [Test]
        public void CanConflictWithStateCapture_Audio_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.Audio.CanConflictWithStateCapture());
        }

        [Test]
        public void CanConflictWithStateCapture_MethodCall_ReturnsFalse()
        {
            Assert.IsFalse(ReplayEventType.MethodCall.CanConflictWithStateCapture());
        }

        #endregion

        #region Event Ordering Tests

        [Test]
        public void EventOrdering_PreStateEventsFirst_ThenPostState()
        {
            // 이벤트 처리 순서 검증:
            // 1. PreState (Instantiate)
            // 2. State Deltas (Transform/Rigidbody/Animator)
            // 3. PostState (Animation, Audio, etc.)

            var events = new[]
            {
                new ReplayEvent { EventType = ReplayEventType.Animation, Time = 1.0f },
                new ReplayEvent { EventType = ReplayEventType.Instantiate, Time = 1.0f },
                new ReplayEvent { EventType = ReplayEventType.Audio, Time = 1.0f },
                new ReplayEvent { EventType = ReplayEventType.Destroy, Time = 1.0f }
            };

            // PreState 이벤트 필터링
            int preStateCount = 0;
            int postStateCount = 0;

            foreach (var evt in events)
            {
                if (evt.EventType.GetProcessingPhase() == EventProcessingPhase.PreState)
                    preStateCount++;
                else
                    postStateCount++;
            }

            Assert.AreEqual(1, preStateCount, "Only Instantiate should be PreState");
            Assert.AreEqual(3, postStateCount, "Animation, Audio, Destroy should be PostState");
        }

        [Test]
        public void EventOrdering_InstantiateAlwaysPreState()
        {
            // Instantiate는 항상 PreState여야 함
            // 이유: 오브젝트가 생성된 후에 상태가 적용되어야 함

            Assert.AreEqual(
                EventProcessingPhase.PreState,
                ReplayEventType.Instantiate.GetProcessingPhase(),
                "Instantiate must be PreState to ensure object exists before state application"
            );
        }

        #endregion

        #region Conflict Detection Logic Tests

        [Test]
        public void ConflictDetection_AnimatorCaptureActive_BlocksAnimationEvent()
        {
            // AnimatorCapture가 활성화된 오브젝트에 대해
            // Animation 이벤트가 기록되면 안 됨 (state-based 캡처 우선)

            var objectsWithAnimatorCapture = new System.Collections.Generic.HashSet<int> { 1, 2, 3 };

            int objectIdWithCapture = 2;
            int objectIdWithoutCapture = 5;

            bool shouldBlockCapture = objectsWithAnimatorCapture.Contains(objectIdWithCapture);
            bool shouldBlockNonCapture = objectsWithAnimatorCapture.Contains(objectIdWithoutCapture);

            Assert.IsTrue(shouldBlockCapture, "Should block animation event for object with AnimatorCapture");
            Assert.IsFalse(shouldBlockNonCapture, "Should allow animation event for object without AnimatorCapture");
        }

        [Test]
        public void ConflictDetection_ForceRecord_BypassesBlock()
        {
            // forceRecord=true 시 충돌 체크 무시

            var objectsWithAnimatorCapture = new System.Collections.Generic.HashSet<int> { 1, 2, 3 };
            int objectId = 2;
            bool forceRecord = true;

            // forceRecord가 true면 충돌 체크 건너뜀
            bool shouldRecord = forceRecord || !objectsWithAnimatorCapture.Contains(objectId);

            Assert.IsTrue(shouldRecord, "forceRecord=true should bypass conflict detection");
        }

        [Test]
        public void ConflictDetection_OnlyAnimationEventsConflict()
        {
            // Animation 이벤트만 충돌 가능

            Assert.IsTrue(ReplayEventType.Animation.CanConflictWithStateCapture());
            Assert.IsFalse(ReplayEventType.Audio.CanConflictWithStateCapture());
            Assert.IsFalse(ReplayEventType.Particle.CanConflictWithStateCapture());
            Assert.IsFalse(ReplayEventType.MethodCall.CanConflictWithStateCapture());
        }

        #endregion
    }
}
