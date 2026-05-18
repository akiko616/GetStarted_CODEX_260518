using UnityEngine;
using UnityEngine.UI;

namespace TRAINEE
{
    public class UiBase : MonoBehaviour
    {
        [SerializeField] protected Canvas _canvas = null;

        private bool _isInit = false;

        protected bool _isShow = false;
        protected bool _isHide = false;
        protected bool _isBackKeyCancel = false;

        protected RectTransform tmCanvasRect = null;

        public virtual void Init()
        {
            Debug.Log($"==== Ui:{GetType().Name} Init");

            _isInit = true;
            _isShow = false;
            _isHide = false;

            
            if (_canvas != null)
            {
                _canvas.worldCamera = UiManager.Instance.GetUiCamera; // UiManager에서 UiCamera 가져온다.
                _canvas.pixelPerfect = true;
                _canvas.gameObject.layer = LayerMask.NameToLayer("UI");
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler canvasScaler = _canvas.GetComponent<CanvasScaler>();

                float fixedAspectRatio = Const.DEFAULT_RESOLUTION_RATIO;
                float currentAspectRatio = (float)Screen.width / (float)Screen.height;

                if (currentAspectRatio > fixedAspectRatio)
                {
                    canvasScaler.matchWidthOrHeight = 1;
                }
                else if (currentAspectRatio < fixedAspectRatio)
                {
                    canvasScaler.matchWidthOrHeight = 0;
                }

                tmCanvasRect = _canvas.GetComponent<RectTransform>();
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;

                Debug.Log($"=========  Ui:{GetType().Name} Canvas is null =========");
            }
        }

        public virtual void Show()
        {
            if (!_isInit)
            {
                Init();
            }

            this.gameObject.SetActive(true);

            _isShow = true;
            _isHide = false;

        }

        public virtual void Hide()
        {
            _isHide = true;
            _isShow = false;
        }

        public void SetSortingOrder(int _sorting_order)
        {
            if(_canvas != null)
            _canvas.sortingOrder = _sorting_order;
        }
        public virtual void UIComponentRegister(UIComponent component)
        {

        }

        public RectTransform TmCanvasRect
        {
            get { return tmCanvasRect; }
        }
    }
}
