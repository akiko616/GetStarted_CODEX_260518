using UnityEngine;


namespace TRAINEE
{
    public static class SystemLoader
    {
        /// <summary>
        /// 교육생석 클라이언트 초기화 시 필요 매너저 그룹
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeLoad()
        {
            // 1. 기본값은 클라이언트(false)로 둡니다.
            bool isGameInstance = false;

            // 2. 실제 빌드 환경: Scripting Define Symbol에 GAMEINSTANCE가 있거나 유니티 서버 빌드일 때
#if GAMEINSTANCE
            isGameInstance = true;
#else
            isGameInstance = false;
#endif

            // 3. 에디터 테스트 환경: ParrelSync 클론 에디터라면 강제로 서버(true)로 만듭니다.
#if UNITY_EDITOR
            // ParrelSync의 API를 호출하여 현재 실행 중인 에디터가 '클론'인지 확인합니다.
            //if (ParrelSync.ClonesManager.IsClone())
            //{
            //    isGameInstance = true;
            //}
#endif

            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // --- 여기서부터 로직 분리 ---
            if (!isGameInstance)
            {
                // [클라이언트 로직]
                if (currentSceneName != ESceneType.Login.ToString())
                {
                    return;
                }

                string name = "Managers";
                string path = $"{Const.Path.SYSTEM_PREFAB_PATH}{name}";
                SingletonGroup obj = LdResources.Load<SingletonGroup>(path, null);

                if (obj != null)
                {
                    Object.DontDestroyOnLoad(obj.gameObject);
                    Debug.Log("[SystemLoader] 클라이언트 매니저 로드 완료");
                }
            }
            else
            {
                // [서버(GameInstance) 로직]
                string name = "ServerManagers";
                string path = $"{Const.Path.SYSTEM_PREFAB_PATH}{name}";
                SingletonGroup obj = LdResources.Load<SingletonGroup>(path, null);

                if (obj != null)
                {
                    Object.DontDestroyOnLoad(obj.gameObject);
                    Debug.Log("[SystemLoader] 서버 전용 매니저(GameInstance) 로드 완료");
                }
            }

        }
    }
}
