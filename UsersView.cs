using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DrkRft_Shared.Api;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// 유저 관리 뷰 - 온라인 유저 목록 + 킥/브로드캐스트
    /// </summary>
    public class UsersView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;
        private Timer _timer;

        // 좌측: 유저 목록
        private readonly FrameView _listFrame;
        private readonly ListView _usersList;
        private List<string> _userDisplayItems = new List<string>();
        private List<OnlineUserDto> _users = new List<OnlineUserDto>();

        // 우측: 상세 정보 + 액션
        private readonly FrameView _detailFrame;
        private readonly Label _lblDetailAccountId;
        private readonly Label _lblDetailClientId;
        private readonly Label _lblDetailType;
        private readonly Label _lblDetailLoginTime;
        private readonly Label _lblDetailOnline;
        private readonly Button _btnKick;
        private readonly Button _btnBroadcast;
        private readonly Button _btnVirtualUser;
        private readonly Button _btnRemoveVirtual;

        public UsersView(ApiClient client) : base("Users")
        {
            _client = client;

            // 좌측 유저 목록
            _listFrame = new FrameView("Online Users [K=Kick B=Broadcast V=Virtual R=RemoveVirtual]")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = Dim.Fill()
            };

            _usersList = new ListView(_userDisplayItems)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                AllowsMarking = false
            };
            _usersList.SelectedItemChanged += OnUserSelected;
            _listFrame.Add(_usersList);

            // 우측 상세
            _detailFrame = new FrameView("User Detail")
            {
                X = Pos.Right(_listFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _lblDetailAccountId = new Label("Account: -") { X = 1, Y = 1 };
            _lblDetailClientId = new Label("Client ID: -") { X = 1, Y = 2 };
            _lblDetailType = new Label("Type: -") { X = 1, Y = 3 };
            _lblDetailLoginTime = new Label("Login: -") { X = 1, Y = 4 };
            _lblDetailOnline = new Label("Online: -") { X = 1, Y = 5 };

            _btnKick = new Button("_Kick (K)")
            {
                X = 1,
                Y = 8
            };
            _btnKick.Clicked += OnKickClicked;

            _btnBroadcast = new Button("_Broadcast (B)")
            {
                X = Pos.Right(_btnKick) + 2,
                Y = 8
            };
            _btnBroadcast.Clicked += OnBroadcastClicked;

            _btnVirtualUser = new Button("_Virtual User (V)")
            {
                X = 1,
                Y = 10
            };
            _btnVirtualUser.Clicked += OnVirtualUserClicked;

            _btnRemoveVirtual = new Button("_Remove Virtual (R)")
            {
                X = Pos.Right(_btnVirtualUser) + 2,
                Y = 10
            };
            _btnRemoveVirtual.Clicked += OnRemoveVirtualClicked;

            _detailFrame.Add(
                _lblDetailAccountId, _lblDetailClientId, _lblDetailType,
                _lblDetailLoginTime, _lblDetailOnline,
                _btnKick, _btnBroadcast,
                _btnVirtualUser, _btnRemoveVirtual
            );

            Add(_listFrame, _detailFrame);

            // 키보드 단축키
            KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.k || e.KeyEvent.Key == Key.K)
                {
                    OnKickClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.b || e.KeyEvent.Key == Key.B)
                {
                    OnBroadcastClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.v || e.KeyEvent.Key == Key.V)
                {
                    OnVirtualUserClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.r || e.KeyEvent.Key == Key.R)
                {
                    OnRemoveVirtualClicked();
                    e.Handled = true;
                }
            };
        }

        private void OnUserSelected(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < _users.Count)
            {
                var user = _users[args.Item];
                string onlineTime = user.OnlineMinutes < 60
                    ? $"{user.OnlineMinutes:F0} min"
                    : $"{user.OnlineMinutes / 60:F0}h {user.OnlineMinutes % 60:F0}m";
                string virtualText = user.IsVirtual ? " [VIRTUAL]" : "";

                _lblDetailAccountId.Text = $"Account: {user.AccountId}{virtualText}";
                _lblDetailClientId.Text = $"Client ID: {user.ClientId}";
                _lblDetailType.Text = $"Type: {user.AccountTypeText} ({user.AccountType})";
                _lblDetailLoginTime.Text = $"Login: {user.LoginTime:yyyy-MM-dd HH:mm:ss}";
                _lblDetailOnline.Text = $"Online: {onlineTime}";
            }
        }

        private void OnKickClicked()
        {
            int idx = _usersList.SelectedItem;
            if (idx < 0 || idx >= _users.Count) return;

            var user = _users[idx];
            int result = MessageBox.Query("Kick User", $"Kick user '{user.AccountId}'?", "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.KickUserAsync(user.AccountId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        private void OnBroadcastClicked()
        {
            var dialog = new Dialog("Broadcast Notice", 60, 10);
            var label = new Label("Message:") { X = 1, Y = 1 };
            var textField = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(2) };
            var btnSend = new Button("Send");
            var btnCancel = new Button("Cancel");

            btnCancel.Clicked += () => Application.RequestStop();
            btnSend.Clicked += () =>
            {
                string message = textField.Text?.ToString();
                if (string.IsNullOrWhiteSpace(message)) return;

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var resp = await _client.BroadcastAsync(message);
                    Application.MainLoop.Invoke(() =>
                    {
                        string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                        MessageBox.Query("Result", msg, "OK");
                    });
                });
                Application.RequestStop();
            };

            dialog.Add(label, textField);
            dialog.AddButton(btnSend);
            dialog.AddButton(btnCancel);
            Application.Run(dialog);
        }

        private void OnVirtualUserClicked()
        {
            var dlg = new Dialog("Register Virtual User", 60, 12);

            var lblId = new Label("Account ID:") { X = 1, Y = 1 };
            var tfId = new TextField("") { X = 16, Y = 1, Width = 38 };

            var lblType = new Label("Account Type:") { X = 1, Y = 3 };
            var rbType = new RadioGroup(new NStack.ustring[] { "User (0)", "Admin (1)" })
            {
                X = 16, Y = 3
            };

            var btnOk = new Button("Register");
            btnOk.Clicked += () =>
            {
                string accountId = tfId.Text?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    MessageBox.ErrorQuery("Error", "Account ID is required.", "OK");
                    return;
                }

                int accountType = rbType.SelectedItem; // 0=User, 1=Admin
                Application.RequestStop();

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var resp = await _client.RegisterVirtualUserAsync(accountId, accountType);
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

            dlg.Add(lblId, tfId, lblType, rbType);
            dlg.AddButton(btnOk);
            dlg.AddButton(btnCancel);
            Application.Run(dlg);
        }

        private void OnRemoveVirtualClicked()
        {
            int idx = _usersList.SelectedItem;
            if (idx < 0 || idx >= _users.Count) return;

            var user = _users[idx];
            if (!user.IsVirtual)
            {
                MessageBox.ErrorQuery("Error", $"'{user.AccountId}' is not a virtual user. Use Kick for real users.", "OK");
                return;
            }

            int result = MessageBox.Query("Remove Virtual User",
                $"Remove virtual user '{user.AccountId}'?", "Yes", "No");
            if (result != 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.RemoveVirtualUserAsync(user.AccountId);
                Application.MainLoop.Invoke(() =>
                {
                    string msg = resp?.Data?.Message ?? resp?.Error ?? "Unknown error";
                    MessageBox.Query("Result", msg, "OK");
                    Refresh();
                });
            });
        }

        public void Refresh()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetUsersAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        _users = resp.Data;
                        _userDisplayItems.Clear();
                        foreach (var u in _users)
                        {
                            string onlineTime = u.OnlineMinutes < 60
                                ? $"{u.OnlineMinutes:F0}m"
                                : $"{u.OnlineMinutes / 60:F0}h{u.OnlineMinutes % 60:F0}m";
                            string virtualTag = u.IsVirtual ? "[V] " : "    ";
                            _userDisplayItems.Add($"{virtualTag}{u.AccountId,-20} {u.AccountTypeText,-10} {onlineTime,8}");
                        }
                        if (_userDisplayItems.Count == 0)
                        {
                            _userDisplayItems.Add("  (no users online)");
                        }
                        _usersList.SetSource(_userDisplayItems);
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
