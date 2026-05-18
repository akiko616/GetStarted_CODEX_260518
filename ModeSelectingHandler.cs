using UnityEngine;
using TRAINEE;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class ModeSelectingHandler : MonoBehaviour
    {
        [SerializeField] private Button _drillPartBtn;
        [SerializeField] private Button _trmPartBtn;
        [SerializeField] private Button _closeBtn;

        private UiMainExcurtionLayer _mainLayer;

       
        public void Init(UiMainExcurtionLayer layer)
        {
            _mainLayer = layer;
        }

        public void Setup()
        {
            InitAddListeners();
        }

        private void InitAddListeners()
        {
            _drillPartBtn.onClick.AddListener(TrainingModeBtn);
            _trmPartBtn.onClick.AddListener(LearningManagementBtn);
        }


        private void TrainingModeBtn()
        {
            _mainLayer.TurnOnJustOne(_mainLayer.DeploymentHandler);
            UiSubExcurtionLayer subLayer = UiManager.Instance.FindLayer<UiSubExcurtionLayer>(ELayerType.UiSubExcurtionLayer);
            subLayer.SetSubAnnounce(true, AnnounceMode.DrillCreated);

            
        }

        private void LearningManagementBtn()
        {
            _mainLayer.TurnOnJustOne(_mainLayer.ReviewHandler);
        }

        private void CloseBtn()
        {

        }
    }
}
