using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DrkRft_Shared.Api;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// 대시보드 뷰 - 서버 전체 상태 요약
    /// </summary>
    public class DashboardView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;
        private Timer _timer;

        // 좌측: 서버 상태
        private readonly FrameView _statusFrame;
        private readonly Label _lblStatus;
        private readonly Label _lblUptime;
        private readonly Label _lblSessions;
        private readonly Label _lblInstances;
        private readonly Label _lblParties;
        private readonly Label _lblLastUpdate;

        // 우측: 온라인 유저 축약 목록
        private readonly FrameView _usersFrame;
        private readonly ListView _usersList;
        private List<string> _userItems = new List<string>();

        public DashboardView(ApiClient client) : base("Dashboard")
        {
            _client = client;

            // 좌측 서버 상태 패널
            _statusFrame = new FrameView("Server Status")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(40),
                Height = Dim.Fill()
            };

            _lblStatus = new Label("Status: --") { X = 1, Y = 1 };
            _lblUptime = new Label("Uptime: --") { X = 1, Y = 3 };
            _lblSessions = new Label("Sessions: --") { X = 1, Y = 5 };
            _lblInstances = new Label("Instances: --") { X = 1, Y = 7 };
            _lblParties = new Label("Parties: --") { X = 1, Y = 9 };
            _lblLastUpdate = new Label("Updated: --") { X = 1, Y = 11 };

            _statusFrame.Add(_lblStatus, _lblUptime, _lblSessions, _lblInstances, _lblParties, _lblLastUpdate);

            // 우측 유저 목록 패널
            _usersFrame = new FrameView("Online Users")
            {
                X = Pos.Right(_statusFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _usersList = new ListView(_userItems)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _usersFrame.Add(_usersList);

            Add(_statusFrame, _usersFrame);
        }

        public void Refresh()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var statusResp = await _client.GetServerStatusAsync();
                    var usersResp = await _client.GetUsersAsync();

                    Application.MainLoop.Invoke(() =>
                    {
                        if (statusResp?.Success == true)
                        {
                            var s = statusResp.Data;
                            var uptime = TimeSpan.FromSeconds(s.UptimeSeconds);

                            _lblStatus.Text = $"Status: {s.Status}";
                            _lblUptime.Text = $"Uptime: {(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
                            _lblSessions.Text = $"Sessions: {s.ActiveSessions} (Admin: {s.AdminCount}, User: {s.UserCount})";
                            _lblInstances.Text = $"Instances: {s.ActiveInstances}/{s.TotalInstances} active";
                            _lblParties.Text = $"Parties: {s.PartyCount}";
                        }
                        else
                        {
                            _lblStatus.Text = "Status: Connection Error";
                        }

                        if (usersResp?.Success == true && usersResp.Data != null)
                        {
                            _userItems.Clear();
                            foreach (var u in usersResp.Data)
                            {
                                string onlineTime = u.OnlineMinutes < 60
                                    ? $"{u.OnlineMinutes:F0}m"
                                    : $"{u.OnlineMinutes / 60:F0}h {u.OnlineMinutes % 60:F0}m";
                                _userItems.Add($"  {u.AccountId,-20} {u.AccountTypeText,-12} {onlineTime}");
                            }
                            if (_userItems.Count == 0)
                            {
                                _userItems.Add("  (no users online)");
                            }
                            _usersList.SetSource(_userItems);
                        }

                        _lblLastUpdate.Text = $"Updated: {DateTime.Now:HH:mm:ss}";
                        SetNeedsDisplay();
                    });
                }
                catch (Exception)
                {
                    Application.MainLoop.Invoke(() =>
                    {
                        _lblStatus.Text = "Status: Error";
                        _lblLastUpdate.Text = $"Updated: {DateTime.Now:HH:mm:ss} (error)";
                    });
                }
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
