using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TRAINEE
{
    public class LoginScene : SceneBase
    {
        public override void Init()
        {
            base.Init();

#if UNITY_EDITOR || DEV_MODE
            // 에디터 or 개발자 모드 일때만 Dev UI가 보임
#else
            // 자동 씬 전환 전 VR 장비 연결 및 착용여부 확인 
            // 자동 씬 전환
#endif
            // PC Mode 인지 VR Mode인지 확인 필요
            // 기본적으로 PC Mode
            // 장치 연결 시 VR Mode로 유동적으로 변경

            //UiManager.Instance.ShowPopup(EPopupType.UiLoadingPopup).Forget();
            StartLogin().Forget();
        }


        private async UniTask StartLogin()
        {
            var popup = await UiManager.Instance.ShowPopup(EPopupType.UiLoadingPopup);

            var loadingPop = popup as UiLoadingPopup;
            loadingPop?.SetupTemplateImage(ESceneType.Login);

            if (popup != null)
            {
                if(popup is UiLoadingPopup loading)
                {
                    loading.StartLoading(Const.LOADING_TIME_LOGIN).Forget();
                }
            }
        }


    }
}
