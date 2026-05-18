using UnityEngine;
using UnityEngine.EventSystems;

namespace DrillSergeant
{
    public class MinimapHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private MonitoringCameraHandler _targetHandler;

        // 외부(MonitoringCameraHandler)에서 자기를 연결해줌
        public void Init(MonitoringCameraHandler handler)
        {
            _targetHandler = handler;
        }

        // 마우스 들어옴!
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_targetHandler != null)
                _targetHandler.SetHoverState(true);
        }

        // 마우스 나감!
        public void OnPointerExit(PointerEventData eventData)
        {
            if (_targetHandler != null)
                _targetHandler.SetHoverState(false);
        }
    }
}