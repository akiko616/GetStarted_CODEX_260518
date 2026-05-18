using UnityEngine;


namespace TRAINEE
{
    public class AnimationEventReceiver : MonoBehaviour
    {
        private ControllerBase _controller;

        public void Init(ControllerBase controller)
        {
            _controller = controller;
        }

        public void OnAnimationHitEvent()
        {
            if (!Application.isPlaying) return;

            if (_controller != null)
            {
                _controller.OnAnimationHitEvent();
            }
        }

        // (추후 필요하다면 HitEnd도 릴레이 가능)
        public void OnAnimationHitEndEvent()
        {
            if (!Application.isPlaying) return;

            if (_controller != null)
            {
                _controller.OnAnimationHitEndEvent();
            }
        }
    }
}
