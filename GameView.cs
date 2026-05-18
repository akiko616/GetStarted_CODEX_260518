using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DrkRft_Shared.Api;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// 통합 Game 뷰 - 상단: Parties, 하단: Instances (상하 2분할)
    /// </summary>
    public class GameView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;
        private Timer _timer;

        // ═══════════════════════════════════════════════════════════════
        // 상단 패널: Parties
        // ═══════════════════════════════════════════════════════════════
        private readonly FrameView _topPane;

        // 파티 목록 (좌측)
        private readonly FrameView _partyListFrame;
        private readonly ListView _partyList;
        private List<string> _partyDisplayItems = new List<string>();
        private List<PartyInfoDto> _parties = new List<PartyInfoDto>();

        // 파티 상세 (우측)
        private readonly FrameView _partyDetailFrame;
        private readonly Label _lblPartyId;
        private readonly Label _lblPartyName;
        private readonly Label _lblScene;
        private readonly Label _lblMembers;
        private readonly FrameView _membersFrame;
        private readonly ListView _membersList;
        private List<string> _memberDisplayItems = new List<string>();
        private readonly Button _btnCreate;
        private readonly Button _btnDisband;
        private readonly Button _btnRequestInstance;
        private readonly Button _btnWaitInstance;
        private readonly Button _btnAddMember;
        private readonly Button _btnRemoveMember;
        private readonly Button _btnChangeRole;
        private readonly Button _btnConfirm;
        private readonly Button _btnSendWake;
        private readonly Button _btnSendCreated;

        // ═══════════════════════════════════════════════════════════════
        // 하단 패널: Instances
        // ═══════════════════════════════════════════════════════════════
        private readonly FrameView _bottomPane;

        // 인스턴스 목록 (좌측)
        private readonly FrameView _instanceListFrame;
        private readonly ListView _instanceList;
        private List<string> _instanceDisplayItems = new List<string>();
        private List<InstanceInfoDto> _instances = new List<InstanceInfoDto>();

        // 인스턴스 상세 (우측)
        private readonly FrameView _instanceDetailFrame;
        private readonly Label _lblInstanceId;
        private readonly Label _lblPort;
        private readonly Label _lblState;
        private readonly Label _lblInstPartyId;
        private readonly Label _lblStartTime;
        private readonly Label _lblHeartbeat;
        private readonly Label _lblAvailable;
        private readonly Button _btnStop;
        private readonly Button _btnStart;
        private readonly Button _btnAssignParty;
        private readonly Button _btnPause;
        private readonly Button _btnGracefulStop;

        public GameView(ApiClient client) : base("Game")
        {
            _client = client;

            // ───────────────────────────────────────────────────────────
            // 상단 패널: Parties (상단 50%)
            // ───────────────────────────────────────────────────────────
            _topPane = new FrameView("Parties [N=New D=Disband I=Instance Q=Grasp A=AddMbr X=RmvMbr L=Role C=Confirm]")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Percent(50)
            };

            // 파티 리스트 (좌측 40%)
            _partyListFrame = new FrameView("Party List")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(40),
                Height = Dim.Fill()
            };

            _partyList = new ListView(_partyDisplayItems)
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _partyList.SelectedItemChanged += OnPartySelected;
            _partyListFrame.Add(_partyList);

            // 파티 상세 (우측 60%)
            _partyDetailFrame = new FrameView("Party Detail")
            {
                X = Pos.Right(_partyListFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _lblPartyId = new Label("ID: -") { X = 1, Y = 1 };
            _lblPartyName = new Label("Name: -") { X = 1, Y = 2 };
            _lblScene = new Label("Scene: -") { X = 1, Y = 3 };
            _lblMembers = new Label("Members: -") { X = 1, Y = 4 };

            _membersFrame = new FrameView("Members")
            {
                X = 1, Y = 6,
                Width = Dim.Fill(2),
                Height = Dim.Fill(4)
            };
            _membersList = new ListView(_memberDisplayItems)
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _membersFrame.Add(_membersList);

            // Wake/Created 버튼 — 제거됨 (자동 플로우로 대체)
            _btnSendWake = new Button("") { Width = 0, Height = 0 };
            _btnSendCreated = new Button("") { Width = 0, Height = 0 };

            _btnAddMember = new Button("_Add Member (A)")
            {
                X = 1,
                Y = Pos.AnchorEnd(4)
            };
            _btnAddMember.Clicked += OnAddMemberClicked;

            _btnRemoveMember = new Button("Remove Member (X)")
            {
                X = Pos.Right(_btnAddMember) + 2,
                Y = Pos.AnchorEnd(4)
            };
            _btnRemoveMember.Clicked += OnRemoveMemberClicked;

            _btnChangeRole = new Button("Role (L)")
            {
                X = Pos.Right(_btnRemoveMember) + 2,
                Y = Pos.AnchorEnd(4)
            };
            _btnChangeRole.Clicked += OnChangeRoleClicked;

            _btnConfirm = new Button("_Confirm (C)")
            {
                X = Pos.Right(_btnChangeRole) + 2,
                Y = Pos.AnchorEnd(4)
            };
            _btnConfirm.Clicked += OnConfirmClicked;

            _btnCreate = new Button("_New Party (N)")
            {
                X = 1,
                Y = Pos.AnchorEnd(2)
            };
            _btnCreate.Clicked += OnCreateClicked;

            _btnRequestInstance = new Button("_Instance (I)")
            {
                X = Pos.Right(_btnCreate) + 2,
                Y = Pos.AnchorEnd(2)
            };
            _btnRequestInstance.Clicked += OnRequestInstanceClicked;

            _btnWaitInstance = new Button("_Grasp Instance (Q)")
            {
                X = Pos.Right(_btnRequestInstance) + 2,
                Y = Pos.AnchorEnd(2)
            };
            _btnWaitInstance.Clicked += OnWaitInstanceClicked;

            _btnDisband = new Button("_Disband (D)")
            {
                X = Pos.Right(_btnWaitInstance) + 2,
                Y = Pos.AnchorEnd(2)
            };
            _btnDisband.Clicked += OnDisbandClicked;

            _partyDetailFrame.Add(_lblPartyId, _lblPartyName, _lblScene, _lblMembers, _membersFrame,
                _btnAddMember, _btnRemoveMember, _btnChangeRole, _btnConfirm,
                _btnCreate, _btnRequestInstance, _btnWaitInstance, _btnDisband);

            _topPane.Add(_partyListFrame, _partyDetailFrame);

            // ───────────────────────────────────────────────────────────
            // 하단 패널: Instances (하단 50%)
            // ───────────────────────────────────────────────────────────
            _bottomPane = new FrameView("Instances [S=Stop G=Start R=Assign P=Pause E=End]")
            {
                X = 0,
                Y = Pos.Bottom(_topPane),
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            // 인스턴스 리스트 (좌측 50%)
            _instanceListFrame = new FrameView("Instance List")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = Dim.Fill()
            };

            _instanceList = new ListView(_instanceDisplayItems)
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _instanceList.SelectedItemChanged += OnInstanceSelected;
            _instanceListFrame.Add(_instanceList);

            // 인스턴스 상세 (우측 50%)
            _instanceDetailFrame = new FrameView("Instance Detail")
            {
                X = Pos.Right(_instanceListFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _lblInstanceId = new Label("Instance: -") { X = 1, Y = 1 };
            _lblPort = new Label("Port: -") { X = 1, Y = 2 };
            _lblState = new Label("State: -") { X = 1, Y = 3 };
            _lblInstPartyId = new Label("Party: -") { X = 1, Y = 4 };
            _lblStartTime = new Label("Started: -") { X = 1, Y = 5 };
            _lblHeartbeat = new Label("Heartbeat: -") { X = 1, Y = 6 };
            _lblAvailable = new Label("Available: -") { X = 1, Y = 7 };

            // 인스턴스 버튼
            _btnStop = new Button("_Stop (S)")
            {
                X = 1,
                Y = Pos.AnchorEnd(4)
            };
            _btnStop.Clicked += OnStopClicked;

            _btnStart = new Button("Start (_G)")
            {
                X = Pos.Right(_btnStop) + 2,
                Y = Pos.AnchorEnd(4)
            };
            _btnStart.Clicked += OnStartClicked;

            _btnAssignParty = new Button("Assign Pa_rty (R)")
            {
                X = Pos.Right(_btnStart) + 2,
                Y = Pos.AnchorEnd(4)
            };
            _btnAssignParty.Clicked += OnAssignPartyClicked;

            _btnPause = new Button("_Pause (P)")
            {
                X = 1,
                Y = Pos.AnchorEnd(2)
            };
            _btnPause.Clicked += OnPauseClicked;

            _btnGracefulStop = new Button("_End (E)")
            {
                X = Pos.Right(_btnPause) + 2,
                Y = Pos.AnchorEnd(2)
            };
            _btnGracefulStop.Clicked += OnGracefulStopClicked;

            _instanceDetailFrame.Add(
                _lblInstanceId, _lblPort, _lblState, _lblInstPartyId,
                _lblStartTime, _lblHeartbeat, _lblAvailable,
                _btnStop, _btnStart, _btnAssignParty,
                _btnPause, _btnGracefulStop
            );

            _bottomPane.Add(_instanceListFrame, _instanceDetailFrame);

            // ───────────────────────────────────────────────────────────
            // 메인에 상/하단 패널 추가
            // ───────────────────────────────────────────────────────────
            Add(_topPane, _bottomPane);

            // ───────────────────────────────────────────────────────────
            // 키보드 단축키 (Party: N,D,I,A,X,C,W,T / Instance: S,G,R,P,E)
            // ───────────────────────────────────────────────────────────
            KeyPress += (e) =>
            {
                switch (e.KeyEvent.Key)
                {
                    // Party shortcuts
                    case Key.n: case Key.N:
                        OnCreateClicked(); e.Handled = true; break;
                    case Key.d: case Key.D:
                        OnDisbandClicked(); e.Handled = true; break;
                    case Key.i: case Key.I:
                        OnRequestInstanceClicked(); e.Handled = true; break;
                    case Key.a: case Key.A:
                        OnAddMemberClicked(); e.Handled = true; break;
                    case Key.x: case Key.X:
                        OnRemoveMemberClicked(); e.Handled = true; break;
                    case Key.l: case Key.L:
                        OnChangeRoleClicked(); e.Handled = true; break;
                    case Key.c: case Key.C:
                        OnConfirmClicked(); e.Handled = true; break;
                    case Key.q: case Key.Q:
                        OnWaitInstanceClicked(); e.Handled = true; break;

                    // Instance shortcuts
                    case Key.s: case Key.S:
                        OnStopClicked(); e.Handled = true; break;
                    case Key.g: case Key.G:
                        OnStartClicked(); e.Handled = true; break;
                    case Key.r: case Key.R:
                        OnAssignPartyClicked(); e.Handled = true; break;
                    case Key.p: case Key.P:
                        OnPauseClicked(); e.Handled = true; break;
                    case Key.e: case Key.E:
                        OnGracefulStopClicked(); e.Handled = true; break;
                }
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Selection
        // ═══════════════════════════════════════════════════════════════

        private void OnPartySelected(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < _parties.Count)
            {
                var party = _parties[args.Item];
                _lblPartyId.Text = $"ID: {party.PartyId}";
                _lblPartyName.Text = $"Name: {party.PartyName ?? "-"}";
                _lblScene.Text = $"Scene: {party.SceneFile ?? "-"}";
                _lblMembers.Text = $"Members: {party.MemberCount}/{party.MaxPlayer}";

                _memberDisplayItems.Clear();
                if (party.Members != null)
                {
                    foreach (var m in party.Members)
                    {
                        _memberDisplayItems.Add($"  {m.AccountId,-20} [{m.Role}]");
                    }
                }
                if (_memberDisplayItems.Count == 0)
                {
                    _memberDisplayItems.Add("  (no members)");
                }
                _membersList.SetSource(_memberDisplayItems);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Create
        // ═══════════════════════════════════════════════════════════════

        private void OnCreateClicked()
        {
            var dlg = new Dialog("Create Party & Request Instance", 70, 22);

            var lblName = new Label("Party Name:") { X = 1, Y = 1 };
            var tfName = new TextField("") { X = 16, Y = 1, Width = 48 };

            var lblInfo = new Label("Party Info:") { X = 1, Y = 3 };
            var tfInfo = new TextField("") { X = 16, Y = 3, Width = 48 };

            var lblScene = new Label("Scene File:") { X = 1, Y = 5 };
            var tfScene = new TextField("") { X = 16, Y = 5, Width = 48 };

            var lblSceneSet = new Label("SceneSet File:") { X = 1, Y = 7 };
            var tfSceneSet = new TextField("") { X = 16, Y = 7, Width = 48 };

            var lblMaxPlayer = new Label("Max Player:") { X = 1, Y = 9 };
            var tfMaxPlayer = new TextField("4") { X = 16, Y = 9, Width = 10 };

            var lblMembersLabel = new Label("Members:") { X = 1, Y = 11 };
            var lblMembersHelp = new Label("(accountId:role, comma separated)") { X = 16, Y = 11 };
            var tfMembers = new TextField("") { X = 16, Y = 12, Width = 48 };

            var btnOk = new Button("Create");
            btnOk.Clicked += () =>
            {
                string partyName = tfName.Text?.ToString()?.Trim() ?? "";
                string partyInfo = tfInfo.Text?.ToString()?.Trim() ?? "";
                string sceneFile = tfScene.Text?.ToString()?.Trim() ?? "";
                string sceneSetFile = tfSceneSet.Text?.ToString()?.Trim() ?? "";
                string maxPlayerStr = tfMaxPlayer.Text?.ToString()?.Trim() ?? "4";
                string membersStr = tfMembers.Text?.ToString()?.Trim() ?? "";

                if (!int.TryParse(maxPlayerStr, out int maxPlayer) || maxPlayer < 1)
                {
                    maxPlayer = 4;
                }

                Dictionary<string, string> members = null;
                if (!string.IsNullOrWhiteSpace(membersStr))
                {
                    members = new Dictionary<string, string>();
                    var parts = membersStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        int colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string accId = trimmed.Substring(0, colonIdx).Trim();
                            string role = trimmed.Substring(colonIdx + 1).Trim();
                            if (!string.IsNullOrEmpty(accId))
                                members[accId] = role;
                        }
                        else if (!string.IsNullOrEmpty(trimmed))
                        {
                            members[trimmed] = "";
                        }
                    }
                }

                var req = new CreatePartyRequest
                {
                    PartyName = partyName,
                    PartyInfo = partyInfo,
                    SceneFile = sceneFile,
                    SceneSetFile = sceneSetFile,
                    MaxPlayer = maxPlayer,
                    Members = members,
                    RequestInstance = false
                };

                Application.RequestStop();

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var resp = await _client.CreatePartyAsync(req);
                    Application.MainLoop.Invoke(() =>
                    {
                        if (resp?.Success == true && resp.Data != null)
                        {
                            string msg = resp.Data.Message ?? "Party created.";
                            if (resp.Data.Instance != null)
                            {
                                msg += $"\nInstance ID: {resp.Data.Instance.InstanceId}, Port: {resp.Data.Instance.Port}";
                            }
                            MessageBox.Query("Success", msg, "OK");
                        }
                        else
                        {
                            string err = resp?.Error ?? "Failed to create party.";
                            MessageBox.ErrorQuery("Error", err, "OK");
                        }
                        Refresh();
                    });
                });
            };

            var btnCancel = new Button("Cancel");
            btnCancel.Clicked += () => Application.RequestStop();

            dlg.Add(lblName, tfName, lblInfo, tfInfo, lblScene, tfScene,
                lblSceneSet, tfSceneSet, lblMaxPlayer, tfMaxPlayer,
                lblMembersLabel, lblMembersHelp, tfMembers);
            dlg.AddButton(btnOk);
            dlg.AddButton(btnCancel);

            Application.Run(dlg);
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Request Instance
        // ═══════════════════════════════════════════════════════════════

        private void OnRequestInstanceClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            int result = MessageBox.Query("Request Instance",
                $"Request a game instance for party '{party.PartyName ?? party.PartyId.ToString()}'?\nScene: {party.SceneFile ?? "(none)"}",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.RequestInstanceAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        string msg = $"Instance assigned!\nID: {resp.Data.InstanceId}\nPort: {resp.Data.Port}\nState: {resp.Data.State}";
                        MessageBox.Query("Success", msg, "OK");
                    }
                    else
                    {
                        string err = resp?.Error ?? "Failed to request instance.";
                        MessageBox.ErrorQuery("Error", err, "OK");
                    }
                    Refresh();
                });
            });
        }

        private void OnWaitInstanceClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            int result = MessageBox.Query("Wait Instance",
                $"Reserve instance slot for party '{party.PartyName ?? party.PartyId.ToString()}'?\n(Will assign when dedicated server connects)",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.WaitInstanceAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true)
                    {
                        MessageBox.Query("Success", $"Instance reserved for party {party.PartyId}.", "OK");
                    }
                    else
                    {
                        string err = resp?.Error ?? "Failed to reserve instance.";
                        MessageBox.ErrorQuery("Error", err, "OK");
                    }
                    Refresh();
                });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Add/Remove Member
        // ═══════════════════════════════════════════════════════════════

        private void OnAddMemberClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var usersResp = await _client.GetUsersAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (usersResp?.Success != true || usersResp.Data == null || usersResp.Data.Count == 0)
                    {
                        MessageBox.ErrorQuery("Error", "No users available.", "OK");
                        return;
                    }

                    var existingMembers = party.Members != null
                        ? new HashSet<string>(party.Members.Select(m => m.AccountId), StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var availableUsers = usersResp.Data
                        .Where(u => !existingMembers.Contains(u.AccountId))
                        .ToList();

                    if (availableUsers.Count == 0)
                    {
                        MessageBox.ErrorQuery("Error", "No available users to add (all already in this party).", "OK");
                        return;
                    }

                    var dlg = new Dialog("Add Member to Party", 70, 18);

                    var lblUser = new Label("Select User:") { X = 1, Y = 1 };
                    var userItems = availableUsers.Select(u =>
                    {
                        string vTag = u.IsVirtual ? "[V] " : "    ";
                        return $"{vTag}{u.AccountId} ({u.AccountTypeText})";
                    }).ToList();
                    var userListView = new ListView(userItems)
                    {
                        X = 1, Y = 2, Width = Dim.Fill(2), Height = 8
                    };

                    var lblRole = new Label("Role:") { X = 1, Y = 11 };
                    var tfRole = new TextField("") { X = 10, Y = 11, Width = 30 };

                    var btnOk = new Button("Add");
                    btnOk.Clicked += () =>
                    {
                        int selIdx = userListView.SelectedItem;
                        if (selIdx < 0 || selIdx >= availableUsers.Count) return;

                        var selectedUser = availableUsers[selIdx];
                        string role = tfRole.Text?.ToString()?.Trim() ?? "";
                        Application.RequestStop();

                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            var resp = await _client.AddPartyMemberAsync(party.PartyId, selectedUser.AccountId, role);
                            Application.MainLoop.Invoke(() =>
                            {
                                string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                                MessageBox.Query("Result", msg, "OK");
                                Refresh();
                            });
                        });
                    };

                    var btnCancel = new Button("Cancel");
                    btnCancel.Clicked += () => Application.RequestStop();

                    dlg.Add(lblUser, userListView, lblRole, tfRole);
                    dlg.AddButton(btnOk);
                    dlg.AddButton(btnCancel);
                    Application.Run(dlg);
                });
            });
        }

        private void OnChangeRoleClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            if (party.Members == null || party.Members.Count == 0)
            {
                MessageBox.ErrorQuery("Error", "Party has no members.", "OK");
                return;
            }

            var dlg = new Dialog("Change Member Role", 60, 14);

            var lblMember = new Label("Select Member:") { X = 1, Y = 1 };
            var memberItems = party.Members.Select(m => $"{m.AccountId} [{m.Role}]").ToList();
            var memberListView = new ListView(memberItems)
            {
                X = 1, Y = 2, Width = Dim.Fill(2), Height = 5
            };

            var lblRole = new Label("New Role:") { X = 1, Y = 8 };
            var tfRole = new TextField("") { X = 12, Y = 8, Width = 30 };

            var btnOk = new Button("OK");
            btnOk.Clicked += () =>
            {
                int selIdx = memberListView.SelectedItem;
                if (selIdx < 0 || selIdx >= party.Members.Count) return;

                var selectedMember = party.Members[selIdx];
                string newRole = tfRole.Text?.ToString()?.Trim() ?? "";
                Application.RequestStop();

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var resp = await _client.ChangeRoleAsync(party.PartyId, selectedMember.AccountId, newRole);
                    Application.MainLoop.Invoke(() =>
                    {
                        if (resp?.Success == true)
                            MessageBox.Query("Success", $"Role of '{selectedMember.AccountId}' changed to '{newRole}'.", "OK");
                        else
                            MessageBox.ErrorQuery("Error", resp?.Error ?? "Failed to change role.", "OK");
                        Refresh();
                    });
                });
            };

            var btnCancel = new Button("Cancel");
            btnCancel.Clicked += () => Application.RequestStop();

            dlg.Add(lblMember, memberListView, lblRole, tfRole);
            dlg.AddButton(btnOk);
            dlg.AddButton(btnCancel);
            Application.Run(dlg);
        }

        private void OnRemoveMemberClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            if (party.Members == null || party.Members.Count == 0)
            {
                MessageBox.ErrorQuery("Error", "Party has no members.", "OK");
                return;
            }

            var dlg = new Dialog("Remove Member from Party", 60, 16);

            var lblMember = new Label("Select Member:") { X = 1, Y = 1 };
            var memberItems = party.Members.Select(m => $"  {m.AccountId,-20} [{m.Role}]").ToList();
            var memberListView = new ListView(memberItems)
            {
                X = 1, Y = 2, Width = Dim.Fill(2), Height = 8
            };

            var btnOk = new Button("Remove");
            btnOk.Clicked += () =>
            {
                int selIdx = memberListView.SelectedItem;
                if (selIdx < 0 || selIdx >= party.Members.Count) return;

                var selectedMember = party.Members[selIdx];
                int confirm = MessageBox.Query("Confirm",
                    $"Remove '{selectedMember.AccountId}' from party '{party.PartyName ?? party.PartyId.ToString()}'?",
                    "Yes", "No");
                if (confirm != 0) return;

                Application.RequestStop();

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var resp = await _client.RemovePartyMemberAsync(party.PartyId, selectedMember.AccountId);
                    Application.MainLoop.Invoke(() =>
                    {
                        string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                        MessageBox.Query("Result", msg, "OK");
                        Refresh();
                    });
                });
            };

            var btnCancel = new Button("Cancel");
            btnCancel.Clicked += () => Application.RequestStop();

            dlg.Add(lblMember, memberListView);
            dlg.AddButton(btnOk);
            dlg.AddButton(btnCancel);
            Application.Run(dlg);
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Confirm
        // ═══════════════════════════════════════════════════════════════

        private void OnConfirmClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];

            if (party.MemberCount == 0)
            {
                MessageBox.ErrorQuery("Error", $"Party '{party.PartyName ?? party.PartyId.ToString()}' has no members to confirm.", "OK");
                return;
            }

            int result = MessageBox.Query("Confirm Party",
                $"Send PartyConfirm to all members of '{party.PartyName ?? party.PartyId.ToString()}'?\n({party.MemberCount} members)",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.ConfirmPartyAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Send Wake/Created to Trainees
        // ═══════════════════════════════════════════════════════════════

        private void OnSendWakeClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            int result = MessageBox.Query("Send WakeResult (TAG 227)",
                $"Send TrainingWakeResult to trainees of '{party.PartyName ?? party.PartyId.ToString()}'?",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.SendWakeResultAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                });
            });
        }

        private void OnSendCreatedClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count)
            {
                MessageBox.ErrorQuery("Error", "No party selected.", "OK");
                return;
            }

            var party = _parties[idx];
            int result = MessageBox.Query("Send Created (TAG 229)",
                $"Send TrainingCreated to trainees of '{party.PartyName ?? party.PartyId.ToString()}'?",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.SendCreatedAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Party: Disband
        // ═══════════════════════════════════════════════════════════════

        private void OnDisbandClicked()
        {
            int idx = _partyList.SelectedItem;
            if (idx < 0 || idx >= _parties.Count) return;

            var party = _parties[idx];
            int result = MessageBox.Query("Disband Party",
                $"Disband party '{party.PartyName ?? party.PartyId.ToString()}' ({party.MemberCount} members)?",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.DisbandPartyAsync(party.PartyId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Instance: Selection
        // ═══════════════════════════════════════════════════════════════

        private void OnInstanceSelected(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < _instances.Count)
            {
                var inst = _instances[args.Item];
                _lblInstanceId.Text = $"Instance: #{inst.InstanceId}";
                _lblPort.Text = $"Port: {inst.Port}";
                _lblState.Text = $"State: {inst.State}";
                _lblInstPartyId.Text = $"Party: {(inst.AssignedPartyId.HasValue ? inst.AssignedPartyId.Value.ToString() : "None")}";
                _lblStartTime.Text = $"Started: {(inst.StartTime.HasValue ? inst.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-")}";
                _lblHeartbeat.Text = $"Heartbeat: {(inst.LastHeartbeat.HasValue ? inst.LastHeartbeat.Value.ToString("HH:mm:ss") : "-")}";
                _lblAvailable.Text = $"Available: {(inst.IsAvailable ? "Yes" : "No")}";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Instance: Stop / Start / Assign / Pause / End
        // ═══════════════════════════════════════════════════════════════

        private void OnStopClicked()
        {
            int idx = _instanceList.SelectedItem;
            if (idx < 0 || idx >= _instances.Count) return;

            var inst = _instances[idx];
            if (inst.IsAvailable)
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} is already idle.", "OK");
                return;
            }

            int result = MessageBox.Query("Stop Instance",
                $"Force stop instance #{inst.InstanceId} (Port: {inst.Port}, State: {inst.State})?",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.StopInstanceAsync(inst.InstanceId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        private void OnStartClicked()
        {
            int idx = _instanceList.SelectedItem;
            if (idx < 0 || idx >= _instances.Count) return;

            var inst = _instances[idx];
            if (inst.State != "Ready")
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} is not in Ready state (Current: {inst.State}).\nOnly Ready instances can be started.", "OK");
                return;
            }

            int result = MessageBox.Query("Start Instance",
                $"Force start instance #{inst.InstanceId} (Port: {inst.Port}, Party: {(inst.AssignedPartyId.HasValue ? inst.AssignedPartyId.Value.ToString() : "None")})?",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.StartInstanceAsync(inst.InstanceId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        private void OnPauseClicked()
        {
            int idx = _instanceList.SelectedItem;
            if (idx < 0 || idx >= _instances.Count) return;

            var inst = _instances[idx];
            if (inst.State != "Running")
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} is not Running (Current: {inst.State}).\nOnly Running instances can be paused.", "OK");
                return;
            }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.PauseInstanceAsync(inst.InstanceId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Pause Toggle", msg, "OK");
                    Refresh();
                });
            });
        }

        private void OnGracefulStopClicked()
        {
            int idx = _instanceList.SelectedItem;
            if (idx < 0 || idx >= _instances.Count) return;

            var inst = _instances[idx];
            if (inst.State != "Running")
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} is not Running (Current: {inst.State}).\nOnly Running instances can be gracefully stopped.", "OK");
                return;
            }

            int result = MessageBox.Query("Graceful Stop",
                $"Graceful stop instance #{inst.InstanceId}?\nThis will notify trainees and shut down the instance.",
                "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GracefulStopInstanceAsync(inst.InstanceId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        private void OnAssignPartyClicked()
        {
            int idx = _instanceList.SelectedItem;
            if (idx < 0 || idx >= _instances.Count) return;

            var inst = _instances[idx];

            if (inst.AssignedPartyId.HasValue)
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} already has Party {inst.AssignedPartyId.Value} assigned.", "OK");
                return;
            }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var partiesResp = await _client.GetPartiesAsync();

                Application.MainLoop.Invoke(() =>
                {
                    if (partiesResp?.Success != true || partiesResp.Data == null || partiesResp.Data.Count == 0)
                    {
                        MessageBox.Query("Info", "No parties available.", "OK");
                        return;
                    }

                    var partyItems = new List<string>();
                    foreach (var p in partiesResp.Data)
                    {
                        partyItems.Add($"Party #{p.PartyId} - {p.PartyName} ({p.MemberCount}/{p.MaxPlayer})");
                    }

                    var dlg = new Dialog("Select Party to Assign", 60, 20);

                    var listView = new ListView(partyItems)
                    {
                        X = 1, Y = 1,
                        Width = Dim.Fill(1),
                        Height = Dim.Fill(3)
                    };
                    dlg.Add(listView);

                    var btnOk = new Button("OK");
                    btnOk.Clicked += () =>
                    {
                        int sel = listView.SelectedItem;
                        if (sel < 0 || sel >= partiesResp.Data.Count)
                        {
                            Application.RequestStop();
                            return;
                        }

                        var selectedParty = partiesResp.Data[sel];
                        Application.RequestStop();

                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            var resp = await _client.AssignPartyToInstanceAsync(inst.InstanceId, selectedParty.PartyId);
                            Application.MainLoop.Invoke(() =>
                            {
                                string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                                MessageBox.Query("Result", msg, "OK");
                                Refresh();
                            });
                        });
                    };

                    var btnCancel = new Button("Cancel");
                    btnCancel.Clicked += () => Application.RequestStop();

                    dlg.AddButton(btnOk);
                    dlg.AddButton(btnCancel);
                    Application.Run(dlg);
                });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // Instance: Helper
        // ═══════════════════════════════════════════════════════════════

        private string GetStateIcon(string state)
        {
            switch (state)
            {
                case "Idle": return "[.]";
                case "Starting": return "[>]";
                case "Registered": return "[R]";
                case "Ready": return "[=]";
                case "Running": return "[*]";
                case "Stopping": return "[x]";
                default: return "[?]";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Refresh / Polling
        // ═══════════════════════════════════════════════════════════════

        public void Refresh()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                // 파티 + 인스턴스 동시 요청
                var partiesTask = _client.GetPartiesAsync();
                var instancesTask = _client.GetInstancesAsync();

                var partiesResp = await partiesTask;
                var instancesResp = await instancesTask;

                Application.MainLoop.Invoke(() =>
                {
                    // 파티 갱신
                    if (partiesResp?.Success == true && partiesResp.Data != null)
                    {
                        _parties = partiesResp.Data;
                        _partyDisplayItems.Clear();
                        foreach (var p in _parties)
                        {
                            _partyDisplayItems.Add($"  [{p.PartyId}] {p.PartyName ?? "unnamed",-15} {p.MemberCount}/{p.MaxPlayer} members  {p.SceneFile ?? ""}");
                        }
                        if (_partyDisplayItems.Count == 0)
                        {
                            _partyDisplayItems.Add("  (no parties)");
                        }
                        _partyList.SetSource(_partyDisplayItems);
                    }

                    // 인스턴스 갱신
                    if (instancesResp?.Success == true && instancesResp.Data != null)
                    {
                        _instances = instancesResp.Data.OrderBy(i => i.InstanceId).ToList();
                        _instanceDisplayItems.Clear();
                        foreach (var i in _instances)
                        {
                            string stateIcon = GetStateIcon(i.State);
                            string partyStr = i.AssignedPartyId.HasValue ? $"P:{i.AssignedPartyId}" : "";
                            _instanceDisplayItems.Add(
                                $"  {stateIcon} #{i.InstanceId,-3} :{i.Port}  {i.State,-12} {partyStr}");
                        }
                        if (_instanceDisplayItems.Count == 0)
                        {
                            _instanceDisplayItems.Add("  (no instances)");
                        }
                        _instanceList.SetSource(_instanceDisplayItems);
                    }

                    SetNeedsDisplay();
                });
            });
        }

        public void StartPolling()
        {
            Refresh();
            _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void StopPolling()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
