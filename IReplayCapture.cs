using ReplaySystem.Compression;
using ReplaySystem.Core;

namespace ReplaySystem.Capture
{
    /// <summary>리플레이 캡처 인터페이스입니다.</summary>
    public interface IReplayCapture
    {
        /// <summary>캡처 대상 오브젝트 ID</summary>
        int ObjectId { get; }

        /// <summary>프리팹 이름</summary>
        string PrefabName { get; }

        /// <summary>캡처 초기화</summary>
        void Initialize(int objectId);

        /// <summary>초기 상태 캡처</summary>
        ObjectState CaptureInitialState();

        /// <summary>현재 Transform 상태 캡처 (압축)</summary>
        CompressedTransform CaptureCompressedTransform();

        /// <summary>상태 적용 (재생 시)</summary>
        void ApplyState(CompressedTransform state);

        /// <summary>보간 상태 적용</summary>
        void ApplyInterpolatedState(CompressedTransform from, CompressedTransform to, float t);
    }

    /// <summary>Rigidbody 캡처 인터페이스입니다.</summary>
    public interface IRigidbodyCapture
    {
        /// <summary>연결된 오브젝트 ID</summary>
        int ObjectId { get; }

        /// <summary>Rigidbody 존재 여부</summary>
        bool HasRigidbody { get; }

        /// <summary>현재 Rigidbody 상태 캡처</summary>
        CompressedRigidbody CaptureState();

        /// <summary>Rigidbody 델타 캡처</summary>
        CompressedRigidbodyDelta? CaptureDelta();

        /// <summary>상태 적용</summary>
        void ApplyState(CompressedRigidbody state);

        /// <summary>보간 상태 적용</summary>
        void ApplyInterpolatedState(CompressedRigidbody from, CompressedRigidbody to, float t);

        /// <summary>상태 초기화</summary>
        void ResetState();
    }

    /// <summary>Animator 캡처 인터페이스입니다.</summary>
    public interface IAnimatorCapture
    {
        /// <summary>연결된 오브젝트 ID</summary>
        int ObjectId { get; }

        /// <summary>Animator 존재 여부</summary>
        bool HasAnimator { get; }

        /// <summary>현재 Animator 전체 상태 캡처</summary>
        CompressedAnimatorState CaptureState();

        /// <summary>Animator 델타 캡처 (변경된 경우에만 반환)</summary>
        CompressedAnimatorDelta? CaptureDelta();

        /// <summary>상태 적용</summary>
        void ApplyState(CompressedAnimatorState state);

        /// <summary>보간 상태 적용</summary>
        void ApplyInterpolatedState(CompressedAnimatorState from, CompressedAnimatorState to, float t);

        /// <summary>상태 초기화 (녹화/재생 시작 시 호출)</summary>
        void ResetState();
    }
}
