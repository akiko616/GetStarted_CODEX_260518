using Cysharp.Threading.Tasks;
using DrillSergeant;
using UnityEngine;
using UnityEngine.UI;

namespace TRAINEE
{
    public class UiSergeantLoadingPopup : PopupBase
    {
        [SerializeField] protected Canvas _subCanvas = null;
        [SerializeField] private Image _progressBar;
        [SerializeField] private Image _followBar;

        public override void Show()
        {
            base.Show();

            _subCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _subCanvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _subCanvas.targetDisplay = 1;

            SceneManager.Instance.ChangeScene(ESceneType.SergeantExcurtion, true, async sceneProgress =>
            {
                _progressBar.fillAmount = sceneProgress * 0.3f;
                _followBar.fillAmount = sceneProgress * 0.3f;

                if (sceneProgress >= 1f)
                {
                    await ServerHubManager.Instance.FTPDownLoading(uiProgress =>
                    {
                        _progressBar.fillAmount = Mathf.Lerp(0.3f, 0.6f, uiProgress);
                        _followBar.fillAmount = Mathf.Lerp(0.3f, 0.6f, uiProgress);
                    });

                    // 스프라이트 관련 딕셔너리 작업
                    //SpriteDatas.LocalSpriteDatas.InitializeSpriteLookup();

                    await MonitoringPoolSystem.Instance.WarmUpPool(poolProgress =>
                    {
                        _progressBar.fillAmount = Mathf.Lerp(0.6f, 1f, poolProgress);
                        _followBar.fillAmount = Mathf.Lerp(0.6f, 1f, poolProgress);
                    });

                    // RoleData 셋팅
                    ScenarioRoleData.Instance.Init();

                    // DrillManager에 참조설정
                    SergeantExcurtionScene scene = SceneManager.Instance.GetCurScene() as SergeantExcurtionScene;
                    DrillManager.Instance.SergeantExcurtionScene = scene;

                    await FinishLoading();
                }
            }).Forget();
        }

        private async UniTask FinishLoading()
        {
            await UiManager.Instance.ShowLayer(ELayerType.UiMainExcurtionLayer);
            LayerBase subLayer = await UiManager.Instance.ShowLayer(ELayerType.UiSubExcurtionLayer);

            (subLayer as UiSubExcurtionLayer).SetSubAnnounce(true, AnnounceMode.Select);


            UiManager.Instance.Hide<UiSergeantLoadingPopup>(this);
        }
    }
}