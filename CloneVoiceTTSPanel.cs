using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CloneVoiceTTSPanel : MonoBehaviour
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Inspector
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    [SerializeField] private CloneVoiceTTSClient client;
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  UI References
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    VisualElement _panel;
    Label _statusLabel;
    TextField _idField;
    TextField _textField;
    ScrollView _entryList;
    Button _generateBtn;
    Button _cancelBtn;
    ScrollView _logView;

    // Playlist
    VisualElement _playlistSection;
    ScrollView _playlistView;
    Label _nowPlayingLabel;
    Button _playlistToggleBtn;
    bool _playlistOpen;
    int _lastLibraryCount = -1;
    AudioSource _audioSource;
    string _playingId;

    int _lastEntryCount = -1;
    readonly List<string> _logs = new();
    const int MaxLogs = 50;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    void OnEnable()
    {
        if (!_audioSource)
        {
            _audioSource = GetComponent<AudioSource>();
            if (!_audioSource)
                _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }

        RebuildUI();
        Application.logMessageReceived += OnLogMessage;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessage;
    }

    void Update()
    {
        EnsureUI();

        if (Input.GetKeyDown(toggleKey))
            _panel.style.display = _panel.style.display == DisplayStyle.None
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        RefreshStatus();
        RefreshEntryList();
        RefreshPlaylist();
        RefreshNowPlaying();
    }

    void EnsureUI()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null) return;
        if (doc.rootVisualElement == null || doc.rootVisualElement.childCount == 0)
            RebuildUI();
    }

    void RebuildUI()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null || doc.rootVisualElement == null) return;

        doc.rootVisualElement.Clear();
        _panel = BuildPanel();
        doc.rootVisualElement.Add(_panel);
        _lastEntryCount = -1;
        _lastLibraryCount = -1;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Build UI
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    VisualElement BuildPanel()
    {
        var panel = new VisualElement();
        panel.style.position = Position.Absolute;
        panel.style.top = 10;
        panel.style.right = 10;
        panel.style.width = 600;
        panel.style.maxHeight = new Length(94, LengthUnit.Percent);
        panel.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.92f);
        panel.style.borderTopLeftRadius = 12;
        panel.style.borderTopRightRadius = 12;
        panel.style.borderBottomLeftRadius = 12;
        panel.style.borderBottomRightRadius = 12;
        panel.style.paddingTop = 16;
        panel.style.paddingBottom = 16;
        panel.style.paddingLeft = 18;
        panel.style.paddingRight = 18;
        SetBorder(panel, new Color(0.3f, 0.6f, 1f, 0.5f), 1);

        panel.Add(BuildHeader());
        panel.Add(BuildStatusBar());
        panel.Add(BuildSeparator());
        panel.Add(BuildInputSection());
        panel.Add(BuildSeparator());
        panel.Add(BuildEntryListSection());
        panel.Add(BuildSeparator());
        panel.Add(BuildActionButtons());
        panel.Add(BuildSeparator());
        panel.Add(BuildPlaylistSection());
        panel.Add(BuildSeparator());
        panel.Add(BuildLogSection());

        return panel;
    }

    VisualElement BuildHeader()
    {
        var row = Row();
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 4;

        var title = new Label("Clone Voice TTS");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.7f, 0.85f, 1f);
        row.Add(title);

        var closeBtn = MakeButton("X", () =>
            _panel.style.display = DisplayStyle.None);
        closeBtn.style.width = 34;
        closeBtn.style.height = 32;
        closeBtn.style.fontSize = 16;
        row.Add(closeBtn);

        return row;
    }

    VisualElement BuildStatusBar()
    {
        _statusLabel = new Label("--");
        _statusLabel.style.fontSize = 16;
        _statusLabel.style.paddingTop = 4;
        _statusLabel.style.paddingBottom = 4;
        _statusLabel.style.paddingLeft = 8;
        _statusLabel.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        _statusLabel.style.borderTopLeftRadius = 4;
        _statusLabel.style.borderTopRightRadius = 4;
        _statusLabel.style.borderBottomLeftRadius = 4;
        _statusLabel.style.borderBottomRightRadius = 4;
        _statusLabel.style.marginBottom = 4;
        return _statusLabel;
    }

    VisualElement BuildInputSection()
    {
        var section = new VisualElement();
        section.style.marginTop = 4;
        section.style.marginBottom = 4;

        var lbl = SectionLabel("Add Entry");
        section.Add(lbl);

        _idField = MakeTextField("ID");
        _idField.style.marginBottom = 2;
        section.Add(_idField);

        _textField = MakeTextField("Text");
        _textField.multiline = true;
        _textField.style.height = 80;
        _textField.style.marginBottom = 8;
        section.Add(_textField);

        var addBtn = MakeButton("Add", OnAddClicked);
        addBtn.style.height = 36;
        addBtn.style.fontSize = 16;
        section.Add(addBtn);

        return section;
    }

    VisualElement BuildEntryListSection()
    {
        var section = new VisualElement();
        section.style.marginTop = 4;
        section.style.marginBottom = 4;

        var headerRow = Row();
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;
        headerRow.Add(SectionLabel("Entries"));

        var clearBtn = MakeButton("Clear All", OnClearClicked);
        clearBtn.style.height = 28;
        clearBtn.style.fontSize = 14;
        clearBtn.style.paddingLeft = 6;
        clearBtn.style.paddingRight = 6;
        headerRow.Add(clearBtn);
        section.Add(headerRow);

        _entryList = new ScrollView(ScrollViewMode.Vertical);
        _entryList.style.maxHeight = 250;
        _entryList.style.minHeight = 50;
        _entryList.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        _entryList.style.borderTopLeftRadius = 4;
        _entryList.style.borderTopRightRadius = 4;
        _entryList.style.borderBottomLeftRadius = 4;
        _entryList.style.borderBottomRightRadius = 4;
        _entryList.style.paddingTop = 2;
        _entryList.style.paddingBottom = 2;
        SetBorder(_entryList, new Color(0.25f, 0.25f, 0.3f), 1);
        section.Add(_entryList);

        return section;
    }

    VisualElement BuildActionButtons()
    {
        var row = Row();
        row.style.marginTop = 4;
        row.style.marginBottom = 4;

        _generateBtn = MakeButton("Generate All", OnGenerateClicked);
        _generateBtn.style.flexGrow = 1;
        _generateBtn.style.height = 44;
        _generateBtn.style.fontSize = 18;
        _generateBtn.style.backgroundColor = new Color(0.15f, 0.4f, 0.2f);
        row.Add(_generateBtn);

        _cancelBtn = MakeButton("Cancel", OnCancelClicked);
        _cancelBtn.style.width = 100;
        _cancelBtn.style.height = 44;
        _cancelBtn.style.fontSize = 18;
        _cancelBtn.style.backgroundColor = new Color(0.5f, 0.15f, 0.15f);
        _cancelBtn.SetEnabled(false);
        row.Add(_cancelBtn);

        return row;
    }

    VisualElement BuildLogSection()
    {
        var section = new VisualElement();
        section.style.marginTop = 4;

        section.Add(SectionLabel("Log"));

        _logView = new ScrollView(ScrollViewMode.Vertical);
        _logView.style.maxHeight = 250;
        _logView.style.minHeight = 60;
        _logView.style.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        _logView.style.borderTopLeftRadius = 4;
        _logView.style.borderTopRightRadius = 4;
        _logView.style.borderBottomLeftRadius = 4;
        _logView.style.borderBottomRightRadius = 4;
        _logView.style.paddingTop = 2;
        _logView.style.paddingBottom = 2;
        _logView.style.paddingLeft = 4;
        SetBorder(_logView, new Color(0.25f, 0.25f, 0.3f), 1);
        section.Add(_logView);

        return section;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Playlist
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    VisualElement BuildPlaylistSection()
    {
        var section = new VisualElement();
        section.style.marginTop = 4;
        section.style.marginBottom = 4;

        var headerRow = Row();
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;

        _playlistToggleBtn = MakeButton("Playlist", OnPlaylistToggle);
        _playlistToggleBtn.style.flexGrow = 1;
        _playlistToggleBtn.style.height = 36;
        _playlistToggleBtn.style.fontSize = 16;
        _playlistToggleBtn.style.backgroundColor = new Color(0.2f, 0.25f, 0.4f);
        headerRow.Add(_playlistToggleBtn);

        var stopAllBtn = MakeButton("Stop", OnStopClicked);
        stopAllBtn.style.width = 76;
        stopAllBtn.style.height = 36;
        stopAllBtn.style.fontSize = 14;
        stopAllBtn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
        headerRow.Add(stopAllBtn);

        section.Add(headerRow);

        _nowPlayingLabel = new Label("");
        _nowPlayingLabel.style.fontSize = 14;
        _nowPlayingLabel.style.color = new Color(0.4f, 0.9f, 0.5f);
        _nowPlayingLabel.style.marginTop = 2;
        _nowPlayingLabel.style.marginBottom = 2;
        _nowPlayingLabel.style.display = DisplayStyle.None;
        section.Add(_nowPlayingLabel);

        _playlistSection = new VisualElement();
        _playlistSection.style.display = DisplayStyle.None;

        _playlistView = new ScrollView(ScrollViewMode.Vertical);
        _playlistView.style.maxHeight = 280;
        _playlistView.style.minHeight = 50;
        _playlistView.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        _playlistView.style.borderTopLeftRadius = 4;
        _playlistView.style.borderTopRightRadius = 4;
        _playlistView.style.borderBottomLeftRadius = 4;
        _playlistView.style.borderBottomRightRadius = 4;
        _playlistView.style.paddingTop = 4;
        _playlistView.style.paddingBottom = 4;
        SetBorder(_playlistView, new Color(0.25f, 0.3f, 0.5f), 1);
        _playlistSection.Add(_playlistView);

        section.Add(_playlistSection);
        return section;
    }

    void OnPlaylistToggle()
    {
        _playlistOpen = !_playlistOpen;
        _playlistSection.style.display = _playlistOpen ? DisplayStyle.Flex : DisplayStyle.None;
        _playlistToggleBtn.text = _playlistOpen ? "Playlist  [Close]" : "Playlist";

        if (_playlistOpen)
        {
            _lastLibraryCount = -1;
            RefreshPlaylist();
        }
    }

    void RefreshPlaylist()
    {
        if (!_playlistOpen) return;

        var lib = client != null ? client.Library : null;
        if (lib == null)
        {
            _playlistView.Clear();
            _playlistView.Add(new Label("Library not assigned") { style = { color = new Color(0.6f, 0.6f, 0.6f), fontSize = 12, paddingLeft = 6 } });
            _lastLibraryCount = 0;
            return;
        }

        int count = lib.Entries.Count;
        if (count == _lastLibraryCount) return;
        _lastLibraryCount = count;

        _playlistView.Clear();

        if (count == 0)
        {
            _playlistView.Add(new Label("No clips") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 12, paddingLeft = 6 } });
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var entry = lib.Entries[i];
            var row = Row();
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 4;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            if (i % 2 == 1)
                row.style.backgroundColor = new Color(0.1f, 0.1f, 0.13f);

            string clipId = entry.textId;

            var playBtn = MakeButton("Play", () => OnPlayClicked(clipId));
            playBtn.style.width = 62;
            playBtn.style.height = 30;
            playBtn.style.fontSize = 14;
            playBtn.style.backgroundColor = new Color(0.15f, 0.35f, 0.2f);
            playBtn.style.marginRight = 6;
            row.Add(playBtn);

            var idLbl = new Label(entry.textId);
            idLbl.style.width = 120;
            idLbl.style.fontSize = 16;
            idLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLbl.style.color = new Color(0.8f, 0.9f, 1f);
            idLbl.style.overflow = Overflow.Hidden;
            row.Add(idLbl);

            string textPreview = entry.sourceText != null && entry.sourceText.Length > 30
                ? entry.sourceText[..30] + "..."
                : entry.sourceText ?? "";
            var txtLbl = new Label(textPreview);
            txtLbl.style.flexGrow = 1;
            txtLbl.style.fontSize = 14;
            txtLbl.style.color = new Color(0.55f, 0.55f, 0.6f);
            txtLbl.style.overflow = Overflow.Hidden;
            txtLbl.tooltip = entry.sourceText;
            row.Add(txtLbl);

            bool hasClip = entry.clip != null;
            if (!hasClip)
            {
                playBtn.SetEnabled(false);
                playBtn.text = "--";
            }

            _playlistView.Add(row);
        }
    }

    void RefreshNowPlaying()
    {
        if (_audioSource != null && _audioSource.isPlaying && _playingId != null)
        {
            _nowPlayingLabel.text = $"Now Playing: {_playingId}";
            _nowPlayingLabel.style.display = DisplayStyle.Flex;
        }
        else
        {
            if (_playingId != null)
            {
                _playingId = null;
                _nowPlayingLabel.style.display = DisplayStyle.None;
            }
        }
    }

    void OnPlayClicked(string textId)
    {
        var lib = client?.Library;
        if (lib == null) return;

        var clip = lib[textId];
        if (clip == null) return;

        _audioSource.Stop();
        _audioSource.clip = clip;
        _audioSource.Play();
        _playingId = textId;
    }

    void OnStopClicked()
    {
        if (_audioSource != null)
            _audioSource.Stop();
        _playingId = null;
        _nowPlayingLabel.style.display = DisplayStyle.None;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Refresh
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    void RefreshStatus()
    {
        if (client == null)
        {
            _statusLabel.text = "Client not assigned";
            _statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
            _generateBtn.SetEnabled(false);
            _cancelBtn.SetEnabled(false);
            return;
        }

        bool busy = client.IsBusy;
        bool connected = client.IsConnected;
        int count = client.TextEntries.Count;

        string state = busy ? "Generating..." : connected ? "Connected" : "Idle";
        _statusLabel.text = $"{state}  |  {count} entries";
        _statusLabel.style.color = busy ? new Color(1f, 0.9f, 0.3f)
            : connected ? new Color(0.3f, 1f, 0.5f)
            : new Color(0.7f, 0.7f, 0.7f);

        _generateBtn.SetEnabled(!busy && count > 0);
        _cancelBtn.SetEnabled(busy);
    }

    void RefreshEntryList()
    {
        if (client == null) return;

        var entries = client.TextEntries;
        if (entries.Count == _lastEntryCount) return;
        _lastEntryCount = entries.Count;

        _entryList.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = Row();
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;
            row.style.paddingLeft = 4;

            var idLbl = new Label(entry.id);
            idLbl.style.width = 120;
            idLbl.style.fontSize = 16;
            idLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLbl.style.color = new Color(0.8f, 0.9f, 1f);
            idLbl.style.overflow = Overflow.Hidden;
            row.Add(idLbl);

            string preview = entry.text != null && entry.text.Length > 30
                ? entry.text[..30] + "..."
                : entry.text ?? "";
            var txtLbl = new Label(preview);
            txtLbl.style.flexGrow = 1;
            txtLbl.style.fontSize = 14;
            txtLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            txtLbl.style.overflow = Overflow.Hidden;
            row.Add(txtLbl);

            string removeId = entry.id;
            var rmBtn = MakeButton("X", () => OnRemoveClicked(removeId));
            rmBtn.style.width = 32;
            rmBtn.style.height = 28;
            rmBtn.style.fontSize = 14;
            row.Add(rmBtn);

            _entryList.Add(row);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Log Capture
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    void OnLogMessage(string message, string stackTrace, LogType type)
    {
        if (!message.StartsWith("[TTS]") && !message.StartsWith("[WAV]")) return;

        Color c = type switch
        {
            LogType.Error or LogType.Exception => new Color(1f, 0.4f, 0.4f),
            LogType.Warning => new Color(1f, 0.85f, 0.3f),
            _ => new Color(0.6f, 0.8f, 0.6f)
        };

        if (_logs.Count >= MaxLogs)
            _logs.RemoveAt(0);
        _logs.Add(message);

        if (_logView == null) return;

        if (_logView.childCount >= MaxLogs)
            _logView.RemoveAt(0);

        var lbl = new Label(message);
        lbl.style.fontSize = 14;
        lbl.style.color = c;
        lbl.style.whiteSpace = WhiteSpace.Normal;
        lbl.style.marginBottom = 1;
        _logView.Add(lbl);

        _logView.schedule.Execute(() =>
            _logView.scrollOffset = new Vector2(0, _logView.contentContainer.layout.height));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Button Handlers
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    void OnAddClicked()
    {
        if (client == null) return;

        string id = _idField.value?.Trim();
        string text = _textField.value?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text)) return;

        client.AddText(id, text);
        _idField.value = "";
        _textField.value = "";
        _lastEntryCount = -1;
    }

    void OnRemoveClicked(string id)
    {
        if (client == null) return;
        client.RemoveText(id);
        _lastEntryCount = -1;
    }

    void OnClearClicked()
    {
        if (client == null) return;
        client.ClearTexts();
        _lastEntryCount = -1;
    }

    void OnGenerateClicked()
    {
        if (client == null) return;
        client.GenerateAll();
    }

    void OnCancelClicked()
    {
        if (client == null) return;
        client.Cancel();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Helpers
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    static VisualElement Row()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        return row;
    }

    static Label SectionLabel(string text)
    {
        var lbl = new Label(text);
        lbl.style.fontSize = 16;
        lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
        lbl.style.color = new Color(0.55f, 0.65f, 0.8f);
        lbl.style.marginBottom = 2;
        return lbl;
    }

    static VisualElement BuildSeparator()
    {
        var sep = new VisualElement();
        sep.style.height = 1;
        sep.style.backgroundColor = new Color(0.3f, 0.3f, 0.35f, 0.5f);
        sep.style.marginTop = 2;
        sep.style.marginBottom = 2;
        return sep;
    }

    static TextField MakeTextField(string label)
    {
        var field = new TextField(label);
        field.labelElement.style.minWidth = 50;
        field.labelElement.style.width = 50;
        field.labelElement.style.fontSize = 16;
        field.labelElement.style.color = new Color(0.7f, 0.7f, 0.7f);
        var input = field.Q<VisualElement>(className: "unity-text-field__input");
        if (input != null)
        {
            input.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            input.style.color = Color.white;
            input.style.borderTopLeftRadius = 3;
            input.style.borderTopRightRadius = 3;
            input.style.borderBottomLeftRadius = 3;
            input.style.borderBottomRightRadius = 3;
        }
        return field;
    }

    static Button MakeButton(string text, System.Action onClick)
    {
        var btn = new Button(onClick) { text = text };
        btn.style.backgroundColor = new Color(0.22f, 0.22f, 0.28f);
        btn.style.color = Color.white;
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 4;
        btn.style.borderBottomRightRadius = 4;
        SetBorder(btn, new Color(0.35f, 0.35f, 0.45f), 1);
        return btn;
    }

    static void SetBorder(VisualElement el, Color color, float width)
    {
        el.style.borderTopColor = color;
        el.style.borderBottomColor = color;
        el.style.borderLeftColor = color;
        el.style.borderRightColor = color;
        el.style.borderTopWidth = width;
        el.style.borderBottomWidth = width;
        el.style.borderLeftWidth = width;
        el.style.borderRightWidth = width;
    }
}
