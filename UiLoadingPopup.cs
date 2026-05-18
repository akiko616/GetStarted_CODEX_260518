using Cysharp.Threading.Tasks;
using NetworkResponeData;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiLoadingPopup : PopupBase
    {
        [Header("ProgressBar")]
        [SerializeField] private GameObject _progress;
        [SerializeField] private Image _progressBar;
        [SerializeField] private TMP_Text _msgTxt;

        private int _delayTime = 100;

        private bool _isEffectDelaying = false;
        private float _effectTimer = 0f;
        private ELayerType[] _pendingLayers = null;

        [Header("UIComponent")]
        [SerializeField] private List<LoadingImageTemplate> _loadingImageTemplate;
        public override void Init()
        {
            base.Init();

            SetupLoading(false);
        }

        public override void Show()
        {
            base.Show();


            NetworkManager.Instance.OnTrainingBeginSuccess += OnTrainingStart;
        }

        public async UniTaskVoid StartLoading(int delaytime)
        {
            _delayTime = delaytime;

            await LoadingHelper.LoadingAsync(SetProgress, _delayTime);

            OnLoadingEnd();
        }
        public override void UIComponentRegister(UIComponent component)
        {
            base.UIComponentRegister(component);
        }
        public void SetupTemplateImage(ESceneType sceneType)
        {
            for (int i = 0; i < _loadingImageTemplate.Count; i++)
            {
                if (_loadingImageTemplate[i] == null) continue;

                bool isTarget = _loadingImageTemplate[i].SceneType.Contains(sceneType);

                _loadingImageTemplate[i].gameObject.SetActive(isTarget);

                if (isTarget)
                {
                    _loadingImageTemplate[i].Apply(this);
                }
            }
        }
        public void SetLoadingUI(GameObject progress, Image bar, TMP_Text text)
        {
            _progress = progress;
            _progressBar = bar;
            _msgTxt = text;
        }
        public void SetProgress(float progress, string msg)
        {
            string msgText = msg;
            ESceneType _currentScene = SceneManager.Instance.GetCurScene().SceneType;


            if (_currentScene == ESceneType.TrainingLobby && !string.IsNullOrEmpty(msg))
            {
                bool isLobbyMainLoading = msg.Contains("Phase 1");

                msgText = isLobbyMainLoading ? "로비 로딩 중..." : "로비용 추가 리소스 준비 중...";
            }

            if (progress >= 1f)
            {
                switch (_currentScene)
                {
                    case ESceneType.Login:
                        {
                            msgText = "로비 입장중...";
                        }
                        break;
                    case ESceneType.TrainingLobby:
                        {
                        }
                        break;
                    case ESceneType.Training:
                        {
                            msgText = "훈련 시작 대기중...";
                        }
                        break;
                }
            }

            _progressBar.fillAmount = progress;
            _msgTxt.text = msgText;
            SetupLoading(true);
        }


        public override void Hide()
        {
            NetworkManager.Instance.OnTrainingBeginSuccess -= OnTrainingStart;

            base.Hide();
        }

        private void SetupLoading(bool isActive)
        {
            if (isActive)
            {
                _progress.SetActive(isActive);
                _msgTxt.gameObject.SetActive(isActive);
            }
            else
            {
                _progress.SetActive(isActive);
                _msgTxt.gameObject.SetActive(isActive);
                _progressBar.fillAmount = 0f;
                _msgTxt.text = "";
            }
        }

        private void OnLoadingEnd()
        {
            ESceneType _currentScene = SceneManager.Instance.GetCurScene().SceneType;
            switch (_currentScene)
            {
                case ESceneType.Login:
                    {
                        PopupEffectSetup(new ELayerType[] { ELayerType.UiLoginLayer });
                    }
                    break;
                case ESceneType.TrainingLobby:
                    {
                        PopupEffectSetup(new ELayerType[] { ELayerType.UiInteractionLayer });
                    }
                    break;
                case ESceneType.Training:
                    {
                        NetworkManager.Instance.SendTrainingCreated();
#if DEV_MODE
                        PopupEffectSetup(new ELayerType[] {});
#endif
                    }
                    break;
            }
        }

        private void PopupEffectSetup(ELayerType[] layers, float effectTimer = 0f)
        {
            _pendingLayers = layers;
            _effectTimer = effectTimer;
            _isEffectDelaying = true;
        }

        private void PopupEffect()
        {
            if (_pendingLayers != null)
            {
                for (int i = 0; i < _pendingLayers.Length; i++)
                {
                    UiManager.Instance.ShowLayer(_pendingLayers[i]).Forget();
                }
            }

            SetupLoading(false);
            LoadingHelper.AllClear();
            UiManager.Instance.Hide<UiLoadingPopup>(this);

            if (SceneManager.Instance.GetCurScene().SceneType == ESceneType.TrainingLobby)
            {
                UiManager.Instance.ShowPopup(EPopupType.UiEnterTrainingLobbyPopup).Forget();
            }
#if DEV_MODE
            if (SceneManager.Instance.GetCurScene().SceneType == ESceneType.Training)
            {
                GameManager.Instance.GameStart();
            }
#endif
        }

        private void Update()
        {
            if (_isEffectDelaying)
            {
                _effectTimer += Time.unscaledDeltaTime;

                if (_effectTimer >= 0.5f)
                {
                    _isEffectDelaying = false;
                    PopupEffect();
                }
            }
        }

        private void OnTrainingStart(ResTrainingBegin res)
        {
            Debug.LogWarning("로딩 끝 게임 시작");

            SetupLoading(false);
            LoadingHelper.AllClear();
            UiManager.Instance.Hide<UiLoadingPopup>(this);

            GameManager.Instance.GameStart();
        }
    }
}
