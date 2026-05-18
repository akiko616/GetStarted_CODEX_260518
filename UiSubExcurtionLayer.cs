using TMPro;
using UnityEngine;

namespace TRAINEE
{
    public enum AnnounceMode { Select, DrillCreated }

    public class UiSubExcurtionLayer : LayerBase
    {
        [SerializeField] private TextMeshProUGUI _upperText;
        [SerializeField] private TextMeshProUGUI _lowerText;
        [SerializeField] private TextMeshProUGUI _middleText;

        private string originAnounceText;

        

        private void Start()
        {
            Init();
        }
        public override void Init()
        {
            base.Init();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;            
            _canvas.targetDisplay = 1;

            InitAddListener();
        }

        private void OnEnable()
        {

        }

        private void Update()
        {

        }

        private void InitAddListener()
        {

        }
        private void OnDisable()
        {

        }

        

        public void SetSubAnnounce(bool isActive, AnnounceMode mode = AnnounceMode.Select)
        {
            _upperText.transform.parent.gameObject.SetActive(isActive);

            if (!isActive) return;

            bool isSelectMode = (mode == AnnounceMode.Select);

            _middleText.gameObject.SetActive(!isSelectMode); 
            _upperText.gameObject.SetActive(isSelectMode);   
            _lowerText.gameObject.SetActive(isSelectMode);   
        }


        public override void Show()
        {
            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
        }
    }
}
