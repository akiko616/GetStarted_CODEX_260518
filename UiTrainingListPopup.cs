using Cysharp.Threading.Tasks;
using DrillSergeant;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiTrainingListPopup : PopupBase
    {
        [SerializeField] private GameObject _unitPrefab;
        [SerializeField] private Transform _unitParent;
        [SerializeField] private Button _closeBtn;

        private UiMainExcurtionLayer _mainLayer;
        public override void Init()
        {
            base.Init();
            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _canvas.targetDisplay = 1;

            _closeBtn.onClick.AddListener(CloseBtn);

            _mainLayer = UiManager.Instance.FindLayer<UiMainExcurtionLayer>(ELayerType.UiMainExcurtionLayer);

            List<SaveFileDatas> ftpDatas = ServerHubManager.Instance.FTPDatas;
            for (int i = 0; i < ftpDatas.Count; ++i)
            {
                CreateListUnit(ftpDatas[i]);
            }
        }



        private void CreateListUnit(SaveFileDatas ftpData)
        {
            TrainingDatas trainingData = ftpData.trainingData;

            GameObject prefab = Instantiate(_unitPrefab);
            ListUnit com = prefab.GetComponent<ListUnit>();

            com.Init(ftpData);
            com.CreateBtn.onClick.AddListener(() =>
            {
                _mainLayer.CreateMatchedDrill(ftpData);
                com.CreateBtn.interactable = false;
            });

            prefab.transform.SetParent(_unitParent);
        }

        private void CloseBtn()
        {
            UiManager.Instance.Hide(this);

            UiSubExcurtionLayer subLayer = UiManager.Instance.FindLayer<UiSubExcurtionLayer>(ELayerType.UiSubExcurtionLayer);
            subLayer.SetSubAnnounce(true, AnnounceMode.DrillCreated);
        }




        public override void Show()
        {
            base.Show();

            foreach (ListUnit item in _unitParent.GetComponentsInChildren<ListUnit>())
            {
                item.CreateBtn.interactable = true;
            }
            Debug.Log($"트레이닝 리스트 팝업 쇼");
        }

        public override void Hide()
        {
            base.Hide();
            this.gameObject.SetActive(false);
        }

        private async void PopupEffect()
        {
            await UniTask.Delay(500);

            //UiManager.Instance.Hide<UiLoadingPopup>(this);
        }
    }
}
