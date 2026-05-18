using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DatabasePlugin.Client;
using PartyManagerPlugin;
using GameInstancePlugin;

namespace DatabasePlugin.Client.Runtime
{
    /// <summary>
    /// TraineeClient 네트워크 메시지 송수신 테스트 및 디버깅 전체 화면 UI
    /// VisualElement 기반 UI Toolkit - 다크 테마 (Green 액센트)
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TraineeDebugOverlay : MonoBehaviour
    {
        #region Serialized Fields
        [Header("UI Assets")]
        [SerializeField] private VisualTreeAsset visualTreeAsset;
        [SerializeField] private StyleSheet styleSheet;

        [Header("Settings")]
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private bool autoFindClient = true;
        [SerializeField] private TraineeClient targetClient;
        [SerializeField] private KeyCode toggleKey = KeyCode.F12;
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
        private Label partyLabel;
        private Label gameServerLabel;
        private ScrollView logScrollView;
        private Label logStatsLabel;
        private Label topBarTitle;

        // Input Fields
        private TextField idField;
        private TextField passwordField;

        // Navigation
        private string currentPanel = "log";
        private Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private Dictionary<string, VisualElement> panels = new Dictionary<string, VisualElement>();
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
                Debug.LogError("[TraineeDebugOverlay] VisualTreeAsset is not assigned!");
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
            partyLabel = root.Q<Label>("party-label");
            gameServerLabel = root.Q<Label>("gameserver-label");

            // Top bar
            topBarTitle = root.Q<Label>("top-bar-title");

            // Log panel
            logScrollView = root.Q<ScrollView>("log-scroll");
            logStatsLabel = root.Q<Label>("log-stats");

            // Navigation buttons
            navButtons["log"] = root.Q<Button>("nav-log");
            navButtons["auth"] = root.Q<Button>("nav-auth");
            navButtons["status"] = root.Q<Button>("nav-status");

            // Panels
            panels["log"] = root.Q<VisualElement>("panel-log");
            panels["auth"] = root.Q<VisualElement>("panel-auth");
            panels["status"] = root.Q<VisualElement>("panel-status");

            // Auth fields
            idField = root.Q<TextField>("id-field");
            passwordField = root.Q<TextField>("password-field");
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
            root.Q<Button>("retry-autologin-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    targetClient.RetryAutoLogin();
                    AddLog("[SEND] Retry Auto Login", LogType.Send);
                }
            });

            root.Q<Button>("refresh-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                UpdateConnectionStatus();
                AddLog("[INFO] Status Refreshed", LogType.Info);
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

            // Game Server buttons
            root.Q<Button>("disconnect-gameserver-btn")?.RegisterCallback<ClickEvent>(evt =>
            {
                if (ValidateClient())
                {
                    var handler = FindAnyObjectByType<TraineeClientHandler>();
                    if (handler != null)
                    {
                        handler.DisconnectFromGameServer();
                        AddLog("[SEND] Disconnect from Game Server", LogType.Send);
                    }
                    else
                    {
                        AddLog("[ERROR] TraineeClientHandler not found", LogType.Error);
                    }
                }
            });
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
                    "status" => "Training Status",
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

        public void SetTargetClient(TraineeClient client)
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
            var client = FindAnyObjectByType<TraineeClient>();
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
                    AddLog("[ERROR] No TraineeClient in scene!", LogType.Error);
                    return false;
                }
            }
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
                if (partyLabel != null) partyLabel.text = "No Party";
                if (gameServerLabel != null) gameServerLabel.text = "Not Connected";
                return;
            }

            bool loggedIn = targetClient.IsLoggedIn;
            bool init = targetClient.IsInitialized;
            bool gameConnected = targetClient.IsConnectedToGameServer;

            statusDot.RemoveFromClassList("connected");
            statusDot.RemoveFromClassList("disconnected");
            statusDot.AddToClassList(loggedIn ? "connected" : (init ? "connected" : "disconnected"));

            statusValueLabel.text = loggedIn ? "Logged In" : (init ? "Ready" : "Initializing");

            string acc = targetClient.CurrentAccount?.Id ?? "Not Logged In";
            accountLabel.text = acc;

            if (partyLabel != null)
            {
                if (targetClient.CurrentParty != null)
                    partyLabel.text = $"Party #{targetClient.CurrentParty.PartyId}";
                else
                    partyLabel.text = "No Party";
            }

            if (gameServerLabel != null)
            {
                gameServerLabel.text = gameConnected ? "Connected" : "Not Connected";
                gameServerLabel.RemoveFromClassList("connected");
                gameServerLabel.RemoveFromClassList("disconnected");
                gameServerLabel.AddToClassList(gameConnected ? "connected" : "disconnected");
            }
        }

        private void SubscribeToClient()
        {
            if (targetClient == null) return;

            // Auth Events
            targetClient.OnLoginSuccess += OnLoginSuccess;
            targetClient.OnLoginFailed += OnLoginFailed;

            // Auto Login Events
            targetClient.OnAutoLoginStarted += OnAutoLoginStarted;
            targetClient.OnAutoLoginSuccess += OnAutoLoginSuccess;
            targetClient.OnAutoLoginFailed += OnAutoLoginFailed;

            // Training Control Events
            targetClient.OnPartyConfirmReceived += OnPartyConfirmReceived;
            targetClient.OnPartyConfirmFailed += OnPartyConfirmFailed;
            targetClient.OnTrainingWakeResultReceived += OnTrainingWakeResultReceived;
            targetClient.OnTrainingWakeResultFailed += OnTrainingWakeResultFailed;
            targetClient.OnTrainingCreatedReceived += OnTrainingCreatedReceived;
            targetClient.OnTrainingCreatedFailed += OnTrainingCreatedFailed;
            targetClient.OnTrainingBeginReceived += OnTrainingBeginReceived;

            // Game Server Events
            targetClient.OnGameServerReady += OnGameServerReady;
            targetClient.OnTrainingFailed += OnTrainingFailed;
            targetClient.OnDisconnectedFromGameServer += OnDisconnectedFromGameServer;

            // Error
            targetClient.OnError += OnClientError;
        }

        private void UnsubscribeFromClient()
        {
            if (targetClient == null) return;

            // Auth Events
            targetClient.OnLoginSuccess -= OnLoginSuccess;
            targetClient.OnLoginFailed -= OnLoginFailed;

            // Auto Login Events
            targetClient.OnAutoLoginStarted -= OnAutoLoginStarted;
            targetClient.OnAutoLoginSuccess -= OnAutoLoginSuccess;
            targetClient.OnAutoLoginFailed -= OnAutoLoginFailed;

            // Training Control Events
            targetClient.OnPartyConfirmReceived -= OnPartyConfirmReceived;
            targetClient.OnPartyConfirmFailed -= OnPartyConfirmFailed;
            targetClient.OnTrainingWakeResultReceived -= OnTrainingWakeResultReceived;
            targetClient.OnTrainingWakeResultFailed -= OnTrainingWakeResultFailed;
            targetClient.OnTrainingCreatedReceived -= OnTrainingCreatedReceived;
            targetClient.OnTrainingCreatedFailed -= OnTrainingCreatedFailed;
            targetClient.OnTrainingBeginReceived -= OnTrainingBeginReceived;

            // Game Server Events
            targetClient.OnGameServerReady -= OnGameServerReady;
            targetClient.OnTrainingFailed -= OnTrainingFailed;
            targetClient.OnDisconnectedFromGameServer -= OnDisconnectedFromGameServer;

            // Error
            targetClient.OnError -= OnClientError;
        }
        #endregion

        #region Event Handlers
        // Auth
        private void OnLoginSuccess(AccountResponse acc)
        {
            AddLog($"[OK] Login: {acc.Id} (Type: {acc.AccountType})", LogType.Receive);
            UpdateConnectionStatus();
        }
        private void OnLoginFailed(TraineeClient.ResponseCode c) => AddLog($"[FAILED] Login: {c}", LogType.Error);

        // Auto Login
        private void OnAutoLoginStarted() => AddLog("[INFO] Auto Login Started...", LogType.Info);
        private void OnAutoLoginSuccess(AccountResponse acc)
        {
            AddLog($"[OK] Auto Login Success: {acc.Id}", LogType.Receive);
            UpdateConnectionStatus();
        }
        private void OnAutoLoginFailed(string msg) => AddLog($"[FAILED] Auto Login: {msg}", LogType.Error);

        // Training Control
        private void OnPartyConfirmReceived(Party p)
        {
            AddLog($"[NOTIFY] Party Confirmed: #{p.PartyId} ({p.MemberCount} members)", LogType.Receive);
            UpdateConnectionStatus();
        }
        private void OnPartyConfirmFailed(TraineeClient.TrainingResponseCode c) => AddLog($"[FAILED] Party Confirm: {c}", LogType.Error);

        private void OnTrainingWakeResultReceived(TrainingWakeResultData d)
        {
            AddLog($"[NOTIFY] Training Wake Result: Party #{d.PartyId}, Scene: {d.SceneFile}", LogType.Receive);
        }
        private void OnTrainingWakeResultFailed(TraineeClient.TrainingResponseCode c) => AddLog($"[FAILED] Training Wake Result: {c}", LogType.Error);

        private void OnTrainingCreatedReceived(TrainingCreatedData d)
        {
            AddLog($"[NOTIFY] Training Created: Server {d.ServerAddress}:{d.ServerPort}", LogType.Receive);
        }
        private void OnTrainingCreatedFailed(TraineeClient.TrainingResponseCode c) => AddLog($"[FAILED] Training Created: {c}", LogType.Error);

        private void OnTrainingBeginReceived(int instanceId, int partyId)
        {
            AddLog($"[NOTIFY] Training Begin: Instance #{instanceId}, Party #{partyId}", LogType.Receive);
        }

        // Game Server
        private void OnGameServerReady(GameInstanceData d)
        {
            AddLog($"[OK] Game Server Ready: Instance #{d.InstanceId} Port: {d.Port}", LogType.Receive);
            UpdateConnectionStatus();
        }
        private void OnTrainingFailed(TraineeClient.TrainingResponseCode c) => AddLog($"[FAILED] Training: {c}", LogType.Error);
        private void OnDisconnectedFromGameServer()
        {
            AddLog("[INFO] Disconnected from Game Server", LogType.Info);
            UpdateConnectionStatus();
        }

        // Error
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
    }
}
