using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using DatabasePlugin.Client;
using PartyManagerPlugin;
using GameInstancePlugin;
using Disaster.Network.FTP;

namespace DatabasePlugin.Client.Runtime
{
    /// <summary>
    /// DrillInstructorClient 네트워크 메시지 송수신 테스트 및 디버깅 전체 화면 UI
    /// VisualElement 기반 UI Toolkit - 다크 테마
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DrillInstructorDebugOverlay : MonoBehaviour
    {
        #region Serialized Fields
        [Header("UI Assets")]
        [SerializeField] private VisualTreeAsset visualTreeAsset;
        [SerializeField] private StyleSheet styleSheet;

        [Header("Settings")]
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private bool autoFindClient = true;
        [SerializeField] private DrillInstructorClient targetClient;
        [SerializeField] private KeyCode toggleKey = KeyCode.F12;

        [Header("FTP")]
        [SerializeField] private bool autoFindFtpManager = true;
        [SerializeField] private FtpTransferManager ftpManager;
        #endregion

        #region Private Fields
        private const int MAX_LOG_ENTRIES = 500;

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement fullscreenRoot;

        // Log
        private List<LogEntry> logEntries = new List<LogEntry>();
        private bool autoScroll = true;
        private bool showTimestamp = true;
        private LogType logFilter = LogType.All;

        // UI Elements
        private VisualElement statusDot;
        private Label statusValueLabel;
        private Label accountLabel;
        private ScrollView logScrollView;
        private Label logStatsLabel;
        private Label topBarTitle;

        // Input Fields
        private TextField idField;
        private TextField passwordField;
        private IntegerField accountTypeField;
        private IntegerField partyIdField;
        private IntegerField maxPlayerField;
        private TextField targetAccountField;
        private TextField roleField;
        private IntegerField toPartyIdField;
        private IntegerField trainingPartyIdField;
        private TextField sceneFileField;
        private IntegerField filterTypeField;
        private IntegerField instanceIdField;

        // Navigation
        private string currentPanel = "log";
        private Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private Dictionary<string, VisualElement> panels = new Dictionary<string, VisualElement>();

        // FTP UI Elements
        private Label ftpStatusBadge;
        private DropdownField ftpContentTypeField;
        private TextField ftpFilenameField;
        private TextField ftpContentField;
        private ScrollView ftpResultScroll;
        #endregion

        #region Log Entry
        private enum LogType { All, Send, Receive, Error, Info }

        private class LogEntry
        {
            public DateTime Timestamp;
            public string Message;
            public LogType Type;

            public LogEntry(string message, LogType type)
            {
                Timestamp = DateTime.Now;
                Message = message;
                Type = type;
            }

            public string GetStyleClass() => Type switch
            {
                LogType.Send => "log-send",
                LogType.Receive => "log-receive",
                LogType.Error => "log-error",
                LogType.Info => "log-info",
                _ => ""
            };
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void Start()
        {
            InitializeUI();

            if (autoFindClient && targetClient == null)
                FindClient();

            if (targetClient != null)
                SubscribeToClient();

            if (autoFindFtpManager && ftpManager == null)
                ftpManager = FindAnyObjectByType<FtpTransferManager>();

            if (!showOnStart)
                fullscreenRoot.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                ToggleVisibility();

            if (targetClient == null && autoFindClient)
                FindClient();
        }

        private void OnDestroy()
        {
            UnsubscribeFromClient();
        }
        #endregion

        #region UI Initialization
        private void InitializeUI()
        {
            root = uiDocument.rootVisualElement;
            root.Clear();

            if (visualTreeAsset != null)
            {
                visualTreeAsset.CloneTree(root);
            }
            else
            {
                Debug.LogError("[DrillInstructorDebugOverlay] VisualTreeAsset is not assigned!");
                return;
            }

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // Get root element
            fullscreenRoot = root.Q<VisualElement>("fullscreen-root");

            // Query UI elements
            QueryElements();

            // Setup event handlers
            SetupEventHandlers();

            // Initialize navigation
            ShowPanel("log");
            UpdateConnectionStatus();
        }

        private void QueryElements()
        {
            // Status
            statusDot = root.Q<VisualElement>("status-dot");
            statusValueLabel = root.Q<Label>("status-value");
            accountLabel = root.Q<Label>("account-label");

            // Top bar
            topBarTitle = root.Q<Label>("top-bar-title");

            // Log panel
            logScrollView = root.Q<ScrollView>("log-scroll");
            logStatsLabel = root.Q<Label>("log-stats");

            // Navigation buttons
            navButtons["log"] = root.Q<Button>("nav-log");
            navButtons["auth"] = root.Q<Button>("nav-auth");
            navButtons["party"] = root.Q<Button>("nav-party");
            navButtons["training"] = root.Q<Button>("nav-training");
            navButtons["admin"] = root.Q<Button>("nav-admin");
            navButtons["ftp"] = root.Q<Button>("nav-ftp");

            // Panels
            panels["log"] = root.Q<VisualElement>("panel-log");
            panels["auth"] = root.Q<VisualElement>("panel-auth");
            panels["party"] = root.Q<VisualElement>("panel-party");
            panels["training"] = root.Q<VisualElement>("panel-training");
            panels["admin"] = root.Q<VisualElement>("panel-admin");
            panels["ftp"] = root.Q<VisualElement>("panel-ftp");

            // Auth fields
            idField = root.Q<TextField>("id-field");
            passwordField = root.Q<TextField>("password-field");
            accountTypeField = root.Q<IntegerField>("account-type-field");

            // Party fields
            partyIdField = root.Q<IntegerField>("party-id-field");
            maxPlayerField = root.Q<IntegerField>("max-player-field");
            targetAccountField = root.Q<TextField>("target-account-field");
            roleField = root.Q<TextField>("role-field");
            toPartyIdField = root.Q<IntegerField>("to-party-id-field");

            // Training fields
            trainingPartyIdField = root.Q<IntegerField>("training-party-id-field");
            sceneFileField = root.Q<TextField>("scene-file-field");

            // Admin fields
            filterTypeField = root.Q<IntegerField>("filter-type-field");
            instanceIdField = root.Q<IntegerField>("instance-id-field");

            // FTP fields
            ftpStatusBadge = root.Q<Label>("ftp-status-badge");
            ftpContentTypeField = root.Q<DropdownField>("ftp-content-type");
            ftpFilenameField = root.Q<TextField>("ftp-filename-field");
            ftpContentField = root.Q<TextField>("ftp-content-field");
            ftpResultScroll = root.Q<ScrollView>("ftp-result-scroll");
        }

        private void SetupEventHandlers()
        {
            // Close button
            root.Q<Button>("close-btn")?.RegisterCallback<ClickEvent>(evt => Hide());

            // Navigation
            foreach (var kvp in navButtons)
            {
                string panelId = kvp.Key;
                kvp.Value?.RegisterCallback<ClickEvent>(evt => ShowPanel(panelId));
            }

            // Top bar buttons
            root.Q<Button>("quick-login-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendLogin(idField?.value ?? "test_admin", passwordField?.value ?? "Test1234");
                    AddLog("[SEND] Quick Login", LogType.Send);
                }
            });

            root.Q<Button>("refresh-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyGetList();
                    targetClient.SendGetOnlineUsers(-1);
                    AddLog("[SEND] Refresh All", LogType.Send);
                }
            });

            // Log panel
            root.Q<Toggle>("auto-scroll-toggle")?.RegisterValueChangedCallback(evt => autoScroll = evt.newValue);
            root.Q<Toggle>("timestamp-toggle")?.RegisterValueChangedCallback(evt =>
            {
                showTimestamp = evt.newValue;
                RefreshLogDisplay();
            });

            root.Q<DropdownField>("log-filter")?.RegisterValueChangedCallback(evt =>
            {
                logFilter = evt.newValue switch
                {
                    "Send" => LogType.Send,
                    "Receive" => LogType.Receive,
                    "Error" => LogType.Error,
                    "Info" => LogType.Info,
                    _ => LogType.All
                };
                RefreshLogDisplay();
            });

            root.Q<Button>("clear-log-btn")?.RegisterCallback<ClickEvent>(evt => ClearLogs());

            // Auth buttons
            root.Q<Button>("login-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendLogin(idField.value, passwordField.value);
                    AddLog($"[SEND] Login: {idField.value}", LogType.Send);
                }
            });

            root.Q<Button>("register-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendRegister(idField.value, passwordField.value, accountTypeField.value);
                    AddLog("[SEND] Register", LogType.Send);
                }
            });

            root.Q<Button>("get-account-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendGetAccount(idField.value);
                    AddLog("[SEND] GetAccount", LogType.Send);
                }
            });

            root.Q<Button>("delete-account-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendDeleteAccount(idField.value);
                    AddLog("[SEND] DeleteAccount", LogType.Send);
                }
            });

            // Party buttons
            root.Q<Button>("create-party-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyCreate(maxPlayerField.value);
                    AddLog("[SEND] Create Party", LogType.Send);
                }
            });

            root.Q<Button>("destroy-party-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyDestroy(partyIdField.value);
                    AddLog("[SEND] Destroy Party", LogType.Send);
                }
            });

            root.Q<Button>("get-party-list-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyGetList();
                    AddLog("[SEND] GetPartyList", LogType.Send);
                }
            });

            root.Q<Button>("join-party-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyJoin(targetAccountField.value, partyIdField.value);
                    AddLog("[SEND] Join Party", LogType.Send);
                }
            });

            root.Q<Button>("leave-party-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyLeave(targetAccountField.value, partyIdField.value);
                    AddLog("[SEND] Leave Party", LogType.Send);
                }
            });

            root.Q<Button>("move-party-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyMove(partyIdField.value, toPartyIdField.value, targetAccountField.value);
                    AddLog("[SEND] Move Party", LogType.Send);
                }
            });

            root.Q<Button>("change-role-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyChangeRole(partyIdField.value, targetAccountField.value, roleField.value);
                    AddLog("[SEND] Change Role", LogType.Send);
                }
            });

            // Training buttons
            root.Q<Button>("party-confirm-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendPartyConfirm(trainingPartyIdField.value);
                    AddLog("[SEND] PartyConfirm", LogType.Send);
                }
            });

            root.Q<Button>("training-setup-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendTrainingSetup(trainingPartyIdField.value, sceneFileField.value);
                    AddLog("[SEND] TrainingSetup", LogType.Send);
                }
            });

            root.Q<Button>("training-begin-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendTrainingBegin(trainingPartyIdField.value);
                    AddLog("[SEND] TrainingBegin", LogType.Send);
                }
            });

            root.Q<Button>("legacy-start-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendTrainingStart(trainingPartyIdField.value, sceneFileField.value);
                    AddLog("[SEND] TrainingStart (Legacy)", LogType.Send);
                }
            });

            // Admin buttons
            root.Q<Button>("get-online-users-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendGetOnlineUsers(filterTypeField.value);
                    AddLog("[SEND] GetOnlineUsers", LogType.Send);
                }
            });

            root.Q<Button>("get-all-instances-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendGetAllInstances();
                    AddLog("[SEND] GetAllInstances", LogType.Send);
                }
            });

            root.Q<Button>("spectate-request-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.SendSpectateRequest(instanceIdField.value);
                    AddLog("[SEND] SpectateRequest", LogType.Send);
                }
            });

            // FTP buttons
            root.Q<Button>("ftp-connect-btn")?.RegisterCallback<ClickEvent>(evt => FtpConnectAsync().Forget());
            root.Q<Button>("ftp-disconnect-btn")?.RegisterCallback<ClickEvent>(evt => FtpDisconnectAsync().Forget());
            root.Q<Button>("ftp-upload-string-btn")?.RegisterCallback<ClickEvent>(evt => FtpUploadStringAsync().Forget());
            root.Q<Button>("ftp-download-string-btn")?.RegisterCallback<ClickEvent>(evt => FtpDownloadStringAsync().Forget());
            root.Q<Button>("ftp-download-file-btn")?.RegisterCallback<ClickEvent>(evt => FtpDownloadFileAsync().Forget());
            root.Q<Button>("ftp-list-btn")?.RegisterCallback<ClickEvent>(evt => FtpListAsync().Forget());
            root.Q<Button>("ftp-check-local-btn")?.RegisterCallback<ClickEvent>(evt => FtpCheckLocal());
            root.Q<Button>("ftp-clear-result-btn")?.RegisterCallback<ClickEvent>(evt => ftpResultScroll?.Clear());
        }

        private void ShowPanel(string panelId)
        {
            currentPanel = panelId;

            // Update nav buttons
            foreach (var kvp in navButtons)
            {
                if (kvp.Value == null) continue;
                if (kvp.Key == panelId)
                    kvp.Value.AddToClassList("nav-active");
                else
                    kvp.Value.RemoveFromClassList("nav-active");
            }

            // Update panels
            foreach (var kvp in panels)
            {
                if (kvp.Value == null) continue;
                if (kvp.Key == panelId)
                    kvp.Value.AddToClassList("active");
                else
                    kvp.Value.RemoveFromClassList("active");
            }

            // Update title
            if (topBarTitle != null)
            {
                topBarTitle.text = panelId switch
                {
                    "log" => "Network Log",
                    "auth" => "Authentication",
                    "party" => "Party Management",
                    "training" => "Training Control",
                    "admin" => "Admin Panel",
                    "ftp" => "FTP Transfer Test",
                    _ => "Network Log"
                };
            }
        }
        #endregion

        #region Public Methods
        public void Show()
        {
            if (fullscreenRoot != null)
                fullscreenRoot.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (fullscreenRoot != null)
                fullscreenRoot.style.display = DisplayStyle.None;
        }

        public void ToggleVisibility()
        {
            if (fullscreenRoot == null) return;
            if (fullscreenRoot.style.display == DisplayStyle.None)
                Show();
            else
                Hide();
        }

        public void SetTargetClient(DrillInstructorClient client)
        {
            UnsubscribeFromClient();
            targetClient = client;
            SubscribeToClient();
            UpdateConnectionStatus();
        }
        #endregion

        #region Client Management
        private void FindClient()
        {
            var client = FindAnyObjectByType<DrillInstructorClient>();
            if (client != null && client != targetClient)
            {
                SetTargetClient(client);
                AddLog($"[INFO] Client found: {client.gameObject.name}", LogType.Info);
            }
        }

        private bool ValidateClient()
        {
            if (targetClient == null)
            {
                AddLog("[WARN] Client not found - searching...", LogType.Info);
                FindClient();
                if (targetClient == null)
                {
                    AddLog("[ERROR] No DrillInstructorClient in scene!", LogType.Error);
                    return false;
                }
            }
            // IsInitialized는 Initialize() 호출 여부만 체크함
            // 실제 DarkRift 연결 상태와 다를 수 있으므로 명령 전송 허용
            // 연결 실패 시 OnError 이벤트로 로그 표시됨
            return true;
        }

        private void UpdateConnectionStatus()
        {
            if (statusDot == null || statusValueLabel == null || accountLabel == null) return;

            if (targetClient == null)
            {
                statusDot.RemoveFromClassList("connected");
                statusDot.AddToClassList("disconnected");
                statusValueLabel.text = "No Client";
                accountLabel.text = "Not Logged In";
                accountLabel.RemoveFromClassList("admin");
                return;
            }

            // IsInitialized는 Initialize() 호출 여부
            // 로그인 여부는 CurrentAccount로 확인
            bool hasAccount = targetClient.CurrentAccount != null;
            bool init = targetClient.IsInitialized;

            statusDot.RemoveFromClassList("connected");
            statusDot.RemoveFromClassList("disconnected");
            statusDot.RemoveFromClassList("initializing");

            if (hasAccount)
            {
                statusDot.AddToClassList("connected");
                statusValueLabel.text = "Logged In";
            }
            else if (init)
            {
                statusDot.AddToClassList("initializing");
                statusValueLabel.text = "Initialized";
            }
            else
            {
                statusDot.AddToClassList("disconnected");
                statusValueLabel.text = "Disconnected";
            }

            string acc = targetClient.CurrentAccount?.Id ?? "Not Logged In";
            accountLabel.text = acc;

            if (targetClient.IsAdmin)
                accountLabel.AddToClassList("admin");
            else
                accountLabel.RemoveFromClassList("admin");
        }

        private void SubscribeToClient()
        {
            if (targetClient == null) return;

            targetClient.OnLoginSuccess += OnLoginSuccess;
            targetClient.OnLoginFailed += OnLoginFailed;
            targetClient.OnRegisterSuccess += OnRegisterSuccess;
            targetClient.OnRegisterFailed += OnRegisterFailed;
            targetClient.OnGetAccountSuccess += OnGetAccountSuccess;
            targetClient.OnGetAccountFailed += OnGetAccountFailed;
            targetClient.OnDeleteAccountSuccess += OnDeleteAccountSuccess;
            targetClient.OnDeleteAccountFailed += OnDeleteAccountFailed;

            targetClient.OnPartyCreateSuccess += OnPartyCreateSuccess;
            targetClient.OnPartyCreateFailed += OnPartyCreateFailed;
            targetClient.OnPartyDestroySuccess += OnPartyDestroySuccess;
            targetClient.OnPartyDestroyFailed += OnPartyDestroyFailed;
            targetClient.OnPartyGetListSuccess += OnPartyGetListSuccess;
            targetClient.OnPartyGetListFailed += OnPartyGetListFailed;
            targetClient.OnPartyJoinSuccess += OnPartyJoinSuccess;
            targetClient.OnPartyJoinFailed += OnPartyJoinFailed;
            targetClient.OnPartyLeaveSuccess += OnPartyLeaveSuccess;
            targetClient.OnPartyLeaveFailed += OnPartyLeaveFailed;
            targetClient.OnPartyMoveSuccess += OnPartyMoveSuccess;
            targetClient.OnPartyMoveFailed += OnPartyMoveFailed;
            targetClient.OnPartyChangeRoleSuccess += OnPartyChangeRoleSuccess;
            targetClient.OnPartyChangeRoleFailed += OnPartyChangeRoleFailed;

            targetClient.OnTrainingStartAck += OnTrainingStartAck;
            targetClient.OnGameServerReady += OnGameServerReady;
            targetClient.OnTrainingStartFailed += OnTrainingStartFailed;
            targetClient.OnPartyConfirmSuccess += OnPartyConfirmSuccess;
            targetClient.OnPartyConfirmFailed += OnPartyConfirmFailed;
            targetClient.OnTrainingSetupSuccess += OnTrainingSetupSuccess;
            targetClient.OnTrainingSetupFailed += OnTrainingSetupFailed;
            targetClient.OnTrainingBeginSuccess += OnTrainingBeginSuccess;
            targetClient.OnTrainingBeginFailed += OnTrainingBeginFailed;
            targetClient.OnTrainingStatusReceived += OnTrainingStatusReceived;
            targetClient.OnTrainingBeginReadyReceived += OnTrainingBeginReadyReceived;

            targetClient.OnUserLoginReceived += OnUserLoginReceived;
            targetClient.OnUserLogoutReceived += OnUserLogoutReceived;
            targetClient.OnOnlineUsersReceived += OnOnlineUsersReceived;
            targetClient.OnPartyNotificationReceived += OnPartyNotificationReceived;
            targetClient.OnInstanceListReceived += OnInstanceListReceived;
            targetClient.OnSpectateReady += OnSpectateReady;
            targetClient.OnInstanceSignalReceived += OnInstanceSignalReceived;
            targetClient.OnError += OnClientError;
        }

        private void UnsubscribeFromClient()
        {
            if (targetClient == null) return;

            targetClient.OnLoginSuccess -= OnLoginSuccess;
            targetClient.OnLoginFailed -= OnLoginFailed;
            targetClient.OnRegisterSuccess -= OnRegisterSuccess;
            targetClient.OnRegisterFailed -= OnRegisterFailed;
            targetClient.OnGetAccountSuccess -= OnGetAccountSuccess;
            targetClient.OnGetAccountFailed -= OnGetAccountFailed;
            targetClient.OnDeleteAccountSuccess -= OnDeleteAccountSuccess;
            targetClient.OnDeleteAccountFailed -= OnDeleteAccountFailed;

            targetClient.OnPartyCreateSuccess -= OnPartyCreateSuccess;
            targetClient.OnPartyCreateFailed -= OnPartyCreateFailed;
            targetClient.OnPartyDestroySuccess -= OnPartyDestroySuccess;
            targetClient.OnPartyDestroyFailed -= OnPartyDestroyFailed;
            targetClient.OnPartyGetListSuccess -= OnPartyGetListSuccess;
            targetClient.OnPartyGetListFailed -= OnPartyGetListFailed;
            targetClient.OnPartyJoinSuccess -= OnPartyJoinSuccess;
            targetClient.OnPartyJoinFailed -= OnPartyJoinFailed;
            targetClient.OnPartyLeaveSuccess -= OnPartyLeaveSuccess;
            targetClient.OnPartyLeaveFailed -= OnPartyLeaveFailed;
            targetClient.OnPartyMoveSuccess -= OnPartyMoveSuccess;
            targetClient.OnPartyMoveFailed -= OnPartyMoveFailed;
            targetClient.OnPartyChangeRoleSuccess -= OnPartyChangeRoleSuccess;
            targetClient.OnPartyChangeRoleFailed -= OnPartyChangeRoleFailed;

            targetClient.OnTrainingStartAck -= OnTrainingStartAck;
            targetClient.OnGameServerReady -= OnGameServerReady;
            targetClient.OnTrainingStartFailed -= OnTrainingStartFailed;
            targetClient.OnPartyConfirmSuccess -= OnPartyConfirmSuccess;
            targetClient.OnPartyConfirmFailed -= OnPartyConfirmFailed;
            targetClient.OnTrainingSetupSuccess -= OnTrainingSetupSuccess;
            targetClient.OnTrainingSetupFailed -= OnTrainingSetupFailed;
            targetClient.OnTrainingBeginSuccess -= OnTrainingBeginSuccess;
            targetClient.OnTrainingBeginFailed -= OnTrainingBeginFailed;
            targetClient.OnTrainingStatusReceived -= OnTrainingStatusReceived;
            targetClient.OnTrainingBeginReadyReceived -= OnTrainingBeginReadyReceived;

            targetClient.OnUserLoginReceived -= OnUserLoginReceived;
            targetClient.OnUserLogoutReceived -= OnUserLogoutReceived;
            targetClient.OnOnlineUsersReceived -= OnOnlineUsersReceived;
            targetClient.OnPartyNotificationReceived -= OnPartyNotificationReceived;
            targetClient.OnInstanceListReceived -= OnInstanceListReceived;
            targetClient.OnSpectateReady -= OnSpectateReady;
            targetClient.OnInstanceSignalReceived -= OnInstanceSignalReceived;
            targetClient.OnError -= OnClientError;
        }
        #endregion

        #region Event Handlers
        private void OnLoginSuccess(DatabasePlugin.AccountResponse acc) { AddLog($"[OK] Login: {acc.Id} (Type: {acc.AccountType})", LogType.Receive); UpdateConnectionStatus(); }
        private void OnLoginFailed(DrillInstructorClient.ResponseCode c) => AddLog($"[FAILED] Login: {c}", LogType.Error);
        private void OnRegisterSuccess(DatabasePlugin.AccountResponse acc) => AddLog($"[OK] Register: {acc.Id}", LogType.Receive);
        private void OnRegisterFailed(DrillInstructorClient.ResponseCode c) => AddLog($"[FAILED] Register: {c}", LogType.Error);
        private void OnGetAccountSuccess(DatabasePlugin.AccountResponse acc) => AddLog($"[OK] Account: {acc.Id} (Type: {acc.AccountType})", LogType.Receive);
        private void OnGetAccountFailed(DrillInstructorClient.ResponseCode c) => AddLog($"[FAILED] GetAccount: {c}", LogType.Error);
        private void OnDeleteAccountSuccess() => AddLog("[OK] Account deleted", LogType.Receive);
        private void OnDeleteAccountFailed(DrillInstructorClient.ResponseCode c) => AddLog($"[FAILED] DeleteAccount: {c}", LogType.Error);

        private void OnPartyCreateSuccess(Party p) => AddLog($"[OK] Party #{p.PartyId} created (Max: {p.MaxPlayer})", LogType.Receive);
        private void OnPartyCreateFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] CreateParty: {c}", LogType.Error);
        private void OnPartyDestroySuccess(int id) => AddLog($"[OK] Party #{id} destroyed", LogType.Receive);
        private void OnPartyDestroyFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] DestroyParty: {c}", LogType.Error);
        private void OnPartyGetListSuccess(List<Party> ps)
        {
            AddLog($"[OK] Retrieved {ps.Count} parties", LogType.Receive);
            foreach (var p in ps) AddLog($"  → Party #{p.PartyId}: {p.AccountIdAndRole?.Count ?? 0}/{p.MaxPlayer} players", LogType.Info);
        }
        private void OnPartyGetListFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] GetPartyList: {c}", LogType.Error);
        private void OnPartyJoinSuccess(Party p) => AddLog($"[OK] Joined Party #{p.PartyId}", LogType.Receive);
        private void OnPartyJoinFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] JoinParty: {c}", LogType.Error);
        private void OnPartyLeaveSuccess() => AddLog("[OK] Left party", LogType.Receive);
        private void OnPartyLeaveFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] LeaveParty: {c}", LogType.Error);
        private void OnPartyMoveSuccess(Party p) => AddLog($"[OK] Moved to Party #{p.PartyId}", LogType.Receive);
        private void OnPartyMoveFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] MoveParty: {c}", LogType.Error);
        private void OnPartyChangeRoleSuccess(Party p) => AddLog($"[OK] Role changed in Party #{p.PartyId}", LogType.Receive);
        private void OnPartyChangeRoleFailed(DrillInstructorClient.PartyResponseCode c) => AddLog($"[FAILED] ChangeRole: {c}", LogType.Error);

        private void OnTrainingStartAck(string m) => AddLog($"[OK] Training: {m}", LogType.Receive);
        private void OnGameServerReady(GameInstanceData d) => AddLog($"[OK] Game Server Ready: Instance #{d.InstanceId} Port: {d.Port}", LogType.Receive);
        private void OnTrainingStartFailed(DrillInstructorClient.TrainingResponseCode c) => AddLog($"[FAILED] TrainingStart: {c}", LogType.Error);
        private void OnPartyConfirmSuccess() => AddLog("[OK] Party confirmed", LogType.Receive);
        private void OnPartyConfirmFailed(DrillInstructorClient.TrainingResponseCode c) => AddLog($"[FAILED] PartyConfirm: {c}", LogType.Error);
        private void OnTrainingSetupSuccess() => AddLog("[OK] Training setup complete", LogType.Receive);
        private void OnTrainingSetupFailed(DrillInstructorClient.TrainingResponseCode c) => AddLog($"[FAILED] TrainingSetup: {c}", LogType.Error);
        private void OnTrainingBeginSuccess() => AddLog("[OK] Training begun", LogType.Receive);
        private void OnTrainingBeginFailed(DrillInstructorClient.TrainingResponseCode c) => AddLog($"[FAILED] TrainingBegin: {c}", LogType.Error);
        private void OnTrainingStatusReceived(DrillInstructorClient.TrainingStatusData s) => AddLog($"[INFO] Training Status: {s.ReadyMembers}/{s.TotalMembers} ready", LogType.Receive);
        private void OnTrainingBeginReadyReceived(TrainingBeginReadyData d) => AddLog($"[INFO] Players Connected: {d.ConnectedPlayerCount}/{d.ExpectedPlayerCount}", LogType.Receive);

        private void OnUserLoginReceived(DrillInstructorClient.UserLoginNotification n) => AddLog($"[NOTIFY] User Login: {n.Account.Id}", LogType.Receive);
        private void OnUserLogoutReceived(DrillInstructorClient.UserLogoutNotification n) => AddLog($"[NOTIFY] User Logout: {n.AccountId}", LogType.Receive);
        private void OnOnlineUsersReceived(DrillInstructorClient.OnlineUsersResponse r) => AddLog($"[OK] Online: {r.TotalCount} users (Admins: {r.AdminCount})", LogType.Receive);
        private void OnPartyNotificationReceived(DrillInstructorClient.PartyNotification n) => AddLog($"[NOTIFY] Party: {n.Type}", LogType.Receive);
        private void OnInstanceListReceived(List<GameInstanceData> l)
        {
            AddLog($"[OK] Retrieved {l.Count} instances", LogType.Receive);
            foreach (var i in l) AddLog($"  → Instance #{i.InstanceId}: {i.State}", LogType.Info);
        }
        private void OnSpectateReady(GameInstanceData d) => AddLog($"[OK] Spectate Ready: Instance #{d.InstanceId}", LogType.Receive);
        private void OnInstanceSignalReceived(DrillInstructorClient.InstanceSignalData s) => AddLog($"[SIGNAL] Instance Signal: {s.SignalType}", LogType.Receive);
        private void OnClientError(string e) => AddLog($"[ERROR] {e}", LogType.Error);
        #endregion

        #region Log Management
        private void AddLog(string message, LogType type)
        {
            logEntries.Add(new LogEntry(message, type));
            if (logEntries.Count > MAX_LOG_ENTRIES)
                logEntries.RemoveAt(0);
            RefreshLogDisplay();
        }

        private void RefreshLogDisplay()
        {
            if (logScrollView == null) return;

            logScrollView.Clear();

            int count = 0;
            foreach (var entry in logEntries)
            {
                if (logFilter != LogType.All && entry.Type != logFilter)
                    continue;

                string text = showTimestamp ? $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}" : entry.Message;

                var label = new Label(text);
                label.AddToClassList("log-entry");
                label.AddToClassList(entry.GetStyleClass());

                logScrollView.Add(label);
                count++;
            }

            if (logStatsLabel != null)
                logStatsLabel.text = $"{count} / {logEntries.Count} entries";

            if (autoScroll)
            {
                logScrollView.schedule.Execute(() =>
                {
                    logScrollView.scrollOffset = new Vector2(0, float.MaxValue);
                });
            }
        }

        private void ClearLogs()
        {
            logEntries.Clear();
            RefreshLogDisplay();
            AddLog("[INFO] Log cleared", LogType.Info);
        }
        #endregion

        #region FTP Test Methods
        private FtpContentType GetSelectedContentType()
        {
            if (ftpContentTypeField == null) return FtpContentType.ConfirmScenario;
            return ftpContentTypeField.value switch
            {
                "Media" => FtpContentType.Media,
                "Replay" => FtpContentType.Replay,
                "OriginScenario" => FtpContentType.OriginScenario,
                _ => FtpContentType.ConfirmScenario
            };
        }

        private bool ValidateFtpManager()
        {
            if (ftpManager != null) return true;

            if (autoFindFtpManager)
                ftpManager = FindAnyObjectByType<FtpTransferManager>();

            if (ftpManager == null)
            {
                AddFtpResult("[ERROR] FtpTransferManager not found!", "ftp-result-error");
                AddLog("[ERROR] FtpTransferManager not found", LogType.Error);
                return false;
            }
            return true;
        }

        private void UpdateFtpStatus()
        {
            if (ftpStatusBadge == null) return;
            bool connected = ftpManager != null && ftpManager.IsConnected;
            ftpStatusBadge.text = connected ? "Connected" : "Disconnected";
            ftpStatusBadge.RemoveFromClassList("ftp-badge-connected");
            ftpStatusBadge.RemoveFromClassList("ftp-badge-disconnected");
            ftpStatusBadge.AddToClassList(connected ? "ftp-badge-connected" : "ftp-badge-disconnected");
        }

        private void AddFtpResult(string message, string styleClass = "ftp-result-info")
        {
            if (ftpResultScroll == null) return;
            string text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var label = new Label(text);
            label.AddToClassList("ftp-result-entry");
            label.AddToClassList(styleClass);
            ftpResultScroll.Add(label);
            ftpResultScroll.schedule.Execute(() =>
                ftpResultScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private async UniTaskVoid FtpConnectAsync()
        {
            if (!ValidateFtpManager()) return;
            AddFtpResult("Connecting...");
            try
            {
                await ftpManager.ConnectAsync();
                AddFtpResult("Connected!", "ftp-result-success");
                AddLog("[FTP] Connected", LogType.Info);
            }
            catch (Exception ex)
            {
                AddFtpResult($"Connect failed: {ex.Message}", "ftp-result-error");
            }
            UpdateFtpStatus();
        }

        private async UniTaskVoid FtpDisconnectAsync()
        {
            if (!ValidateFtpManager()) return;
            try
            {
                await ftpManager.DisconnectAsync();
                AddFtpResult("Disconnected", "ftp-result-success");
                AddLog("[FTP] Disconnected", LogType.Info);
            }
            catch (Exception ex)
            {
                AddFtpResult($"Disconnect failed: {ex.Message}", "ftp-result-error");
            }
            UpdateFtpStatus();
        }

        private async UniTaskVoid FtpUploadStringAsync()
        {
            if (!ValidateFtpManager()) return;
            var type = GetSelectedContentType();
            string fileName = ftpFilenameField?.value ?? "test";
            string content = ftpContentField?.value ?? "";

            AddFtpResult($"Uploading string to {type}/{fileName}...");
            AddLog($"[FTP] Upload: {type}/{fileName}", LogType.Send);
            try
            {
                await ftpManager.UploadStringAsync(type, content, fileName);
                AddFtpResult($"Upload complete: {type}/{fileName} ({content.Length} chars)", "ftp-result-success");
                AddLog($"[FTP] Upload OK: {type}/{fileName}", LogType.Receive);
            }
            catch (Exception ex)
            {
                AddFtpResult($"Upload failed: {ex.Message}", "ftp-result-error");
                AddLog($"[FTP] Upload FAILED: {ex.Message}", LogType.Error);
            }
            UpdateFtpStatus();
        }

        private async UniTaskVoid FtpDownloadStringAsync()
        {
            if (!ValidateFtpManager()) return;
            var type = GetSelectedContentType();
            string fileName = ftpFilenameField?.value ?? "test";

            AddFtpResult($"Downloading string from {type}/{fileName}...");
            AddLog($"[FTP] Download: {type}/{fileName}", LogType.Send);
            try
            {
                string result = await ftpManager.DownloadStringAsync(type, fileName);
                if (result != null)
                {
                    AddFtpResult($"Download OK ({result.Length} chars):", "ftp-result-success");
                    // 결과가 길면 잘라서 표시
                    string preview = result.Length > 500 ? result.Substring(0, 500) + "..." : result;
                    AddFtpResult(preview);
                    AddLog($"[FTP] Download OK: {type}/{fileName} ({result.Length} chars)", LogType.Receive);
                }
                else
                {
                    AddFtpResult("Download returned null (file not found)", "ftp-result-error");
                    AddLog($"[FTP] Download NULL: {type}/{fileName}", LogType.Error);
                }
            }
            catch (Exception ex)
            {
                AddFtpResult($"Download failed: {ex.Message}", "ftp-result-error");
                AddLog($"[FTP] Download FAILED: {ex.Message}", LogType.Error);
            }
            UpdateFtpStatus();
        }

        private async UniTaskVoid FtpDownloadFileAsync()
        {
            if (!ValidateFtpManager()) return;
            var type = GetSelectedContentType();
            string fileName = ftpFilenameField?.value ?? "test";

            AddFtpResult($"Downloading file {type}/{fileName} to local...");
            AddLog($"[FTP] DownloadFile: {type}/{fileName}", LogType.Send);
            try
            {
                await ftpManager.DownloadAsync(type, fileName);
                string localPath = ftpManager.FindLocalFile(type, fileName);
                AddFtpResult($"File saved: {localPath ?? "unknown"}", "ftp-result-success");
                AddLog($"[FTP] DownloadFile OK: {localPath}", LogType.Receive);
            }
            catch (Exception ex)
            {
                AddFtpResult($"Download failed: {ex.Message}", "ftp-result-error");
                AddLog($"[FTP] DownloadFile FAILED: {ex.Message}", LogType.Error);
            }
            UpdateFtpStatus();
        }

        private async UniTaskVoid FtpListAsync()
        {
            if (!ValidateFtpManager()) return;
            var type = GetSelectedContentType();

            AddFtpResult($"Listing {type} folder...");
            AddLog($"[FTP] List: {type}", LogType.Send);
            try
            {
                var details = await ftpManager.ListDetailsAsync(type);
                AddFtpResult($"Found {details.Count} items:", "ftp-result-success");
                foreach (var entry in details)
                {
                    string icon = entry.IsDirectory ? "[DIR]" : "[FILE]";
                    string size = entry.IsDirectory ? "" : $" ({entry.Size} bytes)";
                    AddFtpResult($"  {icon} {entry.Name}{size}");
                }
                AddLog($"[FTP] List OK: {type} ({details.Count} items)", LogType.Receive);
            }
            catch (Exception ex)
            {
                AddFtpResult($"List failed: {ex.Message}", "ftp-result-error");
                AddLog($"[FTP] List FAILED: {ex.Message}", LogType.Error);
            }
            UpdateFtpStatus();
        }

        private void FtpCheckLocal()
        {
            if (!ValidateFtpManager()) return;
            var type = GetSelectedContentType();
            string fileName = ftpFilenameField?.value ?? "test";

            bool exists = ftpManager.LocalFileExists(type, fileName);
            string localPath = ftpManager.FindLocalFile(type, fileName);
            string folderPath = ftpManager.GetLocalFolder(type);

            if (exists)
            {
                AddFtpResult($"Local file exists: {localPath}", "ftp-result-success");
            }
            else
            {
                AddFtpResult($"Local file NOT found: {type}/{fileName}", "ftp-result-error");
                AddFtpResult($"Local folder: {folderPath}");
            }

            // 로컬 폴더 파일 목록
            var files = ftpManager.GetLocalFiles(type);
            AddFtpResult($"Local files in {type}: {files.Length} items");
            foreach (var f in files)
            {
                AddFtpResult($"  {System.IO.Path.GetFileName(f)}");
            }
        }
        #endregion
    }
}
