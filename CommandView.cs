using System;
using System.Collections.Generic;
using System.Linq;
using DrkRft_Shared.Api;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// 커맨드 뷰 - 직접 명령어 입력 + 결과 표시
    /// </summary>
    public class CommandView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;

        // 입력
        private readonly Label _lblPrompt;
        private readonly TextField _inputField;

        // 결과 히스토리
        private readonly FrameView _historyFrame;
        private readonly ListView _historyList;
        private readonly List<string> _historyItems = new List<string>();

        // 도움말
        private readonly FrameView _helpFrame;
        private readonly Label _lblHelp;

        public CommandView(ApiClient client) : base("Command")
        {
            _client = client;

            // 상단: 도움말
            _helpFrame = new FrameView("Commands")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 7
            };

            _lblHelp = new Label(
                "  status                  - Server status\n" +
                "  users                   - List online users\n" +
                "  kick <accountId>        - Kick user\n" +
                "  broadcast <message>     - Broadcast notice\n" +
                "  parties                 - List parties\n" +
                "  disband <partyId>       - Disband party\n" +
                "  instances               - List instances\n" +
                "  stop <instanceId>       - Stop instance\n" +
                "  clear                   - Clear history"
            )
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _helpFrame.Add(_lblHelp);

            // 중간: 히스토리
            _historyFrame = new FrameView("Output")
            {
                X = 0,
                Y = Pos.Bottom(_helpFrame),
                Width = Dim.Fill(),
                Height = Dim.Fill(3)
            };

            _historyList = new ListView(_historyItems)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _historyFrame.Add(_historyList);

            // 하단: 입력
            _lblPrompt = new Label("> ")
            {
                X = 0,
                Y = Pos.AnchorEnd(1)
            };

            _inputField = new TextField("")
            {
                X = 2,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill()
            };
            _inputField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    string cmd = _inputField.Text?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        ExecuteCommand(cmd);
                        _inputField.Text = "";
                    }
                    e.Handled = true;
                }
            };

            Add(_helpFrame, _historyFrame, _lblPrompt, _inputField);

            // 초기 메시지
            AppendHistory("[System] Type a command and press Enter. Type 'help' for commands.");
        }

        private void ExecuteCommand(string input)
        {
            AppendHistory($"> {input}");

            string[] parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLower();
            string arg = parts.Length > 1 ? parts[1].Trim() : null;

            switch (cmd)
            {
                case "status":
                    ExecuteStatus();
                    break;

                case "users":
                    ExecuteUsers();
                    break;

                case "kick":
                    if (string.IsNullOrEmpty(arg))
                    {
                        AppendHistory("[Error] Usage: kick <accountId>");
                        return;
                    }
                    ExecuteKick(arg);
                    break;

                case "broadcast":
                    if (string.IsNullOrEmpty(arg))
                    {
                        AppendHistory("[Error] Usage: broadcast <message>");
                        return;
                    }
                    ExecuteBroadcast(arg);
                    break;

                case "parties":
                    ExecuteParties();
                    break;

                case "disband":
                    if (string.IsNullOrEmpty(arg) || !int.TryParse(arg, out _))
                    {
                        AppendHistory("[Error] Usage: disband <partyId>");
                        return;
                    }
                    ExecuteDisband(int.Parse(arg));
                    break;

                case "createparty":
                    ExecuteCreateParty(arg);
                    break;

                case "requestinstance":
                    if (string.IsNullOrEmpty(arg) || !int.TryParse(arg, out _))
                    {
                        AppendHistory("[Error] Usage: requestinstance <partyId>");
                        return;
                    }
                    ExecuteRequestInstance(int.Parse(arg));
                    break;

                case "instances":
                    ExecuteInstances();
                    break;

                case "stop":
                    if (string.IsNullOrEmpty(arg) || !int.TryParse(arg, out _))
                    {
                        AppendHistory("[Error] Usage: stop <instanceId>");
                        return;
                    }
                    ExecuteStop(int.Parse(arg));
                    break;

                case "clear":
                    _historyItems.Clear();
                    _historyList.SetSource(_historyItems);
                    break;

                case "help":
                    AppendHistory("Commands: status, users, kick, broadcast, parties, disband,");
                    AppendHistory("  createparty, requestinstance, instances, stop, clear");
                    AppendHistory("  createparty <name>|<scene>|<maxPlayer>       - create party");
                    AppendHistory("  requestinstance <partyId>                    - assign instance");
                    break;

                default:
                    AppendHistory($"[Error] Unknown command: {cmd}");
                    break;
            }
        }

        private void ExecuteStatus()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetServerStatusAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true)
                    {
                        var s = resp.Data;
                        var up = TimeSpan.FromSeconds(s.UptimeSeconds);
                        AppendHistory($"  Status: {s.Status}");
                        AppendHistory($"  Uptime: {(int)up.TotalHours}h {up.Minutes}m");
                        AppendHistory($"  Sessions: {s.ActiveSessions} (Admin:{s.AdminCount}, User:{s.UserCount})");
                        AppendHistory($"  Instances: {s.ActiveInstances}/{s.TotalInstances}");
                        AppendHistory($"  Parties: {s.PartyCount}");
                    }
                    else
                    {
                        AppendHistory($"[Error] {resp?.Error ?? "Connection failed"}");
                    }
                });
            });
        }

        private void ExecuteUsers()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetUsersAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        AppendHistory($"  Online users: {resp.Data.Count}");
                        foreach (var u in resp.Data)
                        {
                            AppendHistory($"    {u.AccountId,-20} {u.AccountTypeText,-10} {u.OnlineMinutes:F0}m");
                        }
                    }
                    else
                    {
                        AppendHistory($"[Error] {resp?.Error ?? "Failed"}");
                    }
                });
            });
        }

        private void ExecuteKick(string accountId)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.KickUserAsync(accountId);
                Application.MainLoop.Invoke(() =>
                {
                    AppendHistory($"  {resp?.Data?.Message ?? resp?.Error ?? "Failed"}");
                });
            });
        }

        private void ExecuteBroadcast(string message)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.BroadcastAsync(message);
                Application.MainLoop.Invoke(() =>
                {
                    AppendHistory($"  {resp?.Data?.Message ?? resp?.Error ?? "Failed"}");
                });
            });
        }

        private void ExecuteParties()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetPartiesAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        AppendHistory($"  Parties: {resp.Data.Count}");
                        foreach (var p in resp.Data)
                        {
                            AppendHistory($"    [{p.PartyId}] {p.PartyName ?? "unnamed"} - {p.MemberCount}/{p.MaxPlayer} members");
                        }
                    }
                    else
                    {
                        AppendHistory($"[Error] {resp?.Error ?? "Failed"}");
                    }
                });
            });
        }

        private void ExecuteDisband(int partyId)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.DisbandPartyAsync(partyId);
                Application.MainLoop.Invoke(() =>
                {
                    AppendHistory($"  {resp?.Data?.Message ?? resp?.Error ?? "Failed"}");
                });
            });
        }

        private void ExecuteInstances()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetInstancesAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        AppendHistory($"  Instances: {resp.Data.Count}");
                        foreach (var i in resp.Data.OrderBy(x => x.InstanceId))
                        {
                            string party = i.AssignedPartyId.HasValue ? $"P:{i.AssignedPartyId}" : "";
                            AppendHistory($"    #{i.InstanceId} :{i.Port} {i.State,-12} {party}");
                        }
                    }
                    else
                    {
                        AppendHistory($"[Error] {resp?.Error ?? "Failed"}");
                    }
                });
            });
        }

        private void ExecuteStop(int instanceId)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.StopInstanceAsync(instanceId);
                Application.MainLoop.Invoke(() =>
                {
                    AppendHistory($"  {resp?.Data?.Message ?? resp?.Error ?? "Failed"}");
                });
            });
        }

        /// <summary>
        /// createparty name|scene|maxPlayer  또는  createparty name
        /// </summary>
        private void ExecuteCreateParty(string arg)
        {
            string partyName = "";
            string sceneFile = "";
            int maxPlayer = 4;

            if (!string.IsNullOrEmpty(arg))
            {
                var parts = arg.Split('|');
                if (parts.Length >= 1) partyName = parts[0].Trim();
                if (parts.Length >= 2) sceneFile = parts[1].Trim();
                if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out maxPlayer);
            }

            if (string.IsNullOrWhiteSpace(partyName))
            {
                AppendHistory("[Error] Usage: createparty <name>|<scene>|<maxPlayer>");
                return;
            }

            AppendHistory($"  Creating party: {partyName}, scene={sceneFile}, max={maxPlayer} ...");

            var req = new CreatePartyRequest
            {
                PartyName = partyName,
                SceneFile = sceneFile,
                MaxPlayer = maxPlayer > 0 ? maxPlayer : 4,
                RequestInstance = false
            };

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.CreatePartyAsync(req);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        AppendHistory($"  {resp.Data.Message}");
                        if (resp.Data.Party != null)
                            AppendHistory($"    Party ID: {resp.Data.Party.PartyId}");
                        if (resp.Data.Instance != null)
                            AppendHistory($"    Instance: #{resp.Data.Instance.InstanceId} port:{resp.Data.Instance.Port}");
                    }
                    else
                    {
                        AppendHistory($"  [Error] {resp?.Error ?? "Failed"}");
                    }
                });
            });
        }

        private void ExecuteRequestInstance(int partyId)
        {
            AppendHistory($"  Requesting instance for party {partyId} ...");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.RequestInstanceAsync(partyId);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        AppendHistory($"  Instance assigned: #{resp.Data.InstanceId} port:{resp.Data.Port} state:{resp.Data.State}");
                    }
                    else
                    {
                        AppendHistory($"  [Error] {resp?.Error ?? "No available instance or already running"}");
                    }
                });
            });
        }

        private void AppendHistory(string line)
        {
            _historyItems.Add(line);
            _historyList.SetSource(_historyItems);
            // 스크롤을 맨 아래로
            if (_historyItems.Count > 0)
            {
                _historyList.SelectedItem = _historyItems.Count - 1;
            }
            SetNeedsDisplay();
        }

        // IRefreshable - CommandView는 자동 새로고침 불필요
        public void Refresh() { }
        public void StartPolling() { _inputField.SetFocus(); }
        public void StopPolling() { }
    }
}
