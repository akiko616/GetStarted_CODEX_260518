using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DrkRft_Shared.Api;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// 인스턴스 관리 뷰 - 10개 슬롯 상태 + 상세 + 강제 중지 + 파티 할당
    /// </summary>
    public class InstancesView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;
        private Timer _timer;

        // 좌측: 인스턴스 목록
        private readonly FrameView _listFrame;
        private readonly ListView _instanceList;
        private List<string> _instanceDisplayItems = new List<string>();
        private List<InstanceInfoDto> _instances = new List<InstanceInfoDto>();

        // 우측: 상세 정보
        private readonly FrameView _detailFrame;
        private readonly Label _lblInstanceId;
        private readonly Label _lblPort;
        private readonly Label _lblState;
        private readonly Label _lblPartyId;
        private readonly Label _lblStartTime;
        private readonly Label _lblHeartbeat;
        private readonly Label _lblAvailable;
        private readonly Button _btnStop;
        private readonly Button _btnStart;
        private readonly Button _btnAssignParty;
        private readonly Button _btnPause;
        private readonly Button _btnGracefulStop;

        public InstancesView(ApiClient client) : base("Instances")
        {
            _client = client;

            // 좌측 인스턴스 목록
            _listFrame = new FrameView("Game Instances [S=Stop, G=Start, A=Assign, P=Pause, E=End]")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = Dim.Fill()
            };

            _instanceList = new ListView(_instanceDisplayItems)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _instanceList.SelectedItemChanged += OnInstanceSelected;
            _listFrame.Add(_instanceList);

            // 우측 상세
            _detailFrame = new FrameView("Instance Detail")
            {
                X = Pos.Right(_listFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _lblInstanceId = new Label("Instance: -") { X = 1, Y = 1 };
            _lblPort = new Label("Port: -") { X = 1, Y = 2 };
            _lblState = new Label("State: -") { X = 1, Y = 3 };
            _lblPartyId = new Label("Party: -") { X = 1, Y = 4 };
            _lblStartTime = new Label("Started: -") { X = 1, Y = 5 };
            _lblHeartbeat = new Label("Heartbeat: -") { X = 1, Y = 6 };
            _lblAvailable = new Label("Available: -") { X = 1, Y = 7 };

            _btnStop = new Button("_Stop (S)")
            {
                X = 1,
                Y = 10
            };
            _btnStop.Clicked += OnStopClicked;

            _btnStart = new Button("Start (_G)")
            {
                X = 20,
                Y = 10
            };
            _btnStart.Clicked += OnStartClicked;

            _btnAssignParty = new Button("_Assign Party (A)")
            {
                X = 38,
                Y = 10
            };
            _btnAssignParty.Clicked += OnAssignPartyClicked;

            _btnPause = new Button("_Pause (P)")
            {
                X = 1,
                Y = 12
            };
            _btnPause.Clicked += OnPauseClicked;

            _btnGracefulStop = new Button("_End (E)")
            {
                X = 20,
                Y = 12
            };
            _btnGracefulStop.Clicked += OnGracefulStopClicked;

            _detailFrame.Add(
                _lblInstanceId, _lblPort, _lblState, _lblPartyId,
                _lblStartTime, _lblHeartbeat, _lblAvailable,
                _btnStop, _btnStart, _btnAssignParty,
                _btnPause, _btnGracefulStop
            );

            Add(_listFrame, _detailFrame);

            // 키보드 단축키
            KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.s || e.KeyEvent.Key == Key.S)
                {
                    OnStopClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.g || e.KeyEvent.Key == Key.G)
                {
                    OnStartClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.a || e.KeyEvent.Key == Key.A)
                {
                    OnAssignPartyClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.p || e.KeyEvent.Key == Key.P)
                {
                    OnPauseClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.e || e.KeyEvent.Key == Key.E)
                {
                    OnGracefulStopClicked();
                    e.Handled = true;
                }
            };
        }

        private void OnInstanceSelected(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < _instances.Count)
            {
                var inst = _instances[args.Item];
                _lblInstanceId.Text = $"Instance: #{inst.InstanceId}";
                _lblPort.Text = $"Port: {inst.Port}";
                _lblState.Text = $"State: {inst.State}";
                _lblPartyId.Text = $"Party: {(inst.AssignedPartyId.HasValue ? inst.AssignedPartyId.Value.ToString() : "None")}";
                _lblStartTime.Text = $"Started: {(inst.StartTime.HasValue ? inst.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-")}";
                _lblHeartbeat.Text = $"Heartbeat: {(inst.LastHeartbeat.HasValue ? inst.LastHeartbeat.Value.ToString("HH:mm:ss") : "-")}";
                _lblAvailable.Text = $"Available: {(inst.IsAvailable ? "Yes" : "No")}";
            }
        }

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

            // 이미 파티가 할당되어 있으면 알림
            if (inst.AssignedPartyId.HasValue)
            {
                MessageBox.Query("Info", $"Instance #{inst.InstanceId} already has Party {inst.AssignedPartyId.Value} assigned.", "OK");
                return;
            }

            // 파티 목록을 가져와서 선택하게 한다
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

        public void Refresh()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GetInstancesAsync();
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true && resp.Data != null)
                    {
                        _instances = resp.Data.OrderBy(i => i.InstanceId).ToList();
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
