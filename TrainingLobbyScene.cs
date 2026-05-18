using Cysharp.Threading.Tasks;
using NetworkResponeData;
using System;
using UnityEngine;


namespace TRAINEE
{
    public class TrainingLobbyScene : SceneBase
    {
        [SerializeField] TrainingLobbyGameLogic _gameLogic;

        public override void Init()
        {
            base.Init();

#if !GAMEINSTANCE
            NetworkManager.Instance.OnLobbyConnection(Const.LOCAL_HOST, Const.LOCAL_PORT).Forget();


            NetworkManager.Instance.OnPartyConfirmSuccess += OnTrainingSetup;
            NetworkManager.Instance.OnPartyConfirmFailed += OnTrainingSetupFailed;
            NetworkManager.Instance.OnTrainingCreatedSuccess += OnTrainingStart;
            NetworkManager.Instance.OnTrainingCreatedFailed += OnTrainingStartFailed;
#endif
            _gameLogic.Init();

        }

#if !GAMEINSTANCE
        public override void Hide()
        {
            base.Hide();

            NetworkManager.Instance.OnPartyConfirmSuccess -= OnTrainingSetup;
            NetworkManager.Instance.OnPartyConfirmFailed -= OnTrainingSetupFailed;
            NetworkManager.Instance.OnTrainingCreatedSuccess -= OnTrainingStart;
            NetworkManager.Instance.OnTrainingCreatedFailed -= OnTrainingStartFailed;
        }

        public void OnTrainingStart()
        {
            UiInteractionLayer layer = UiManager.Instance.FindLayer<UiInteractionLayer>(ELayerType.UiInteractionLayer);
            layer.OnUnRegisterEvent();

            Debug.Log("훈련 씬 진입 시작");
            SceneManager.Instance.ChangeScene(ESceneType.Training).Forget();
        }

        public async void OnTrainingSetup(ResPartyConfirm result)
        {
            // 확정 받으면 파티 결과에 대해서 저장 및 ftp 서버로부터 새로운 게임 데이터를 받는다.
#if !DrillSergeant

            //기존 로비시나리오 데이터 리셋
            GameManager.Instance.ResetScenarioData();

            GameManager.Instance.PlayerRole = result.party.AccountIdAndRole[GameManager.Instance.PlayerID];

            string serverData = await NetworkManager.Instance.FTPManager.DownloadStringAsync(Disaster.Network.FTP.FtpContentType.ConfirmScenario, result.party.SceneSetFile);

            if (!string.IsNullOrEmpty(serverData))
            {
                Debug.Log($"FTP 다운로드 완료!!");
                Debug.Log($"서버 데이터 : {serverData}");
                DataManager.Instance.ServerData = serverData;
                DataManager.Instance.OnServerDataSetup();
            }
#endif
            Debug.Log("훈련 데이터 설정 성공");
        }

        public void OnTrainingSetupFailed(ResPartyConfirm result)
        {
            Debug.LogError("훈련 데이터 설정 실패");
        }

        public void OnTrainingStartFailed(ResTrainingCreated result)
        {
            Debug.Log("훈련 씬 진입 실패");
        }

#endif
    }
}

