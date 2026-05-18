using System;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// TUI 메인 윈도우 - MenuBar + StatusBar + 뷰 전환
    /// </summary>
    public class MainWindow : Toplevel
    {
        private readonly ApiClient _client;
        private readonly string _host;
        private readonly MenuBar _menuBar;
        private readonly StatusBar _statusBar;
        private readonly View _contentArea;

        // 뷰 인스턴스
        private DashboardView _dashboardView;
        private UsersView _usersView;
        private GameView _gameView;
        private CommandView _commandView;
        private TrainingEndView _trainingEndView;

        private View _currentView;
        private string _currentViewName = "Dashboard";

        public MainWindow(ApiClient client, string host)
        {
            _client = client;
            _host = host;

            // 메뉴바
            _menuBar = new MenuBar(new MenuBarItem[]
            {
                new MenuBarItem("_Server", new MenuItem[]
                {
                    new MenuItem("_Dashboard", "Server overview", () => SwitchView("Dashboard")),
                    new MenuItem("_Change Host", "Change API server URL", ShowChangeHostDialog),
                    null, // separator
                    new MenuItem("_Quit", "Exit TUI", () => RequestStop())
                }),
                new MenuBarItem("_Users", new MenuItem[]
                {
                    new MenuItem("_User List", "Online users", () => SwitchView("Users"))
                }),
                new MenuBarItem("_Game", new MenuItem[]
                {
                    new MenuItem("_Parties & Instances", "Parties and game instances", () => SwitchView("Game"))
                }),
                new MenuBarItem("_Command", new MenuItem[]
                {
                    new MenuItem("_Command Input", "Direct commands", () => SwitchView("Command"))
                }),
                new MenuBarItem("_Training", new MenuItem[]
                {
                    new MenuItem("_Training End", "TrainResultPush / GracefulEnd", () => SwitchView("TrainingEnd"))
                })
            });
            Add(_menuBar);

            // 상태바
            _statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.F1, "~F1~ Dashboard", () => SwitchView("Dashboard")),
                new StatusItem(Key.F2, "~F2~ Users", () => SwitchView("Users")),
                new StatusItem(Key.F3, "~F3~ Game", () => SwitchView("Game")),
                new StatusItem(Key.F4, "~F4~ Command", () => SwitchView("Command")),
                new StatusItem(Key.F5, "~F5~ TrainingEnd", () => SwitchView("TrainingEnd")),
                new StatusItem(Key.F6, "~F6~ Refresh", RefreshCurrentView),
                new StatusItem(Key.F10, "~F10~ Quit", () => RequestStop())
            });
            Add(_statusBar);

            // 콘텐츠 영역
            _contentArea = new View()
            {
                X = 0,
                Y = 1, // 메뉴바 아래
                Width = Dim.Fill(),
                Height = Dim.Fill(1) // 상태바 위
            };
            Add(_contentArea);

            // 초기 뷰: 대시보드
            SwitchView("Dashboard");
        }

        private void SwitchView(string viewName)
        {
            // 기존 뷰 정리
            if (_currentView != null)
            {
                StopViewPolling(_currentView);
                _contentArea.Remove(_currentView);
            }

            _currentViewName = viewName;

            switch (viewName)
            {
                case "Dashboard":
                    if (_dashboardView == null) _dashboardView = new DashboardView(_client);
                    _currentView = _dashboardView;
                    break;
                case "Users":
                    if (_usersView == null) _usersView = new UsersView(_client);
                    _currentView = _usersView;
                    break;
                case "Game":
                    if (_gameView == null) _gameView = new GameView(_client);
                    _currentView = _gameView;
                    break;
                case "Command":
                    if (_commandView == null) _commandView = new CommandView(_client);
                    _currentView = _commandView;
                    break;
                case "TrainingEnd":
                    if (_trainingEndView == null) _trainingEndView = new TrainingEndView(_client);
                    _currentView = _trainingEndView;
                    break;
            }

            if (_currentView != null)
            {
                _currentView.X = 0;
                _currentView.Y = 0;
                _currentView.Width = Dim.Fill();
                _currentView.Height = Dim.Fill();
                _contentArea.Add(_currentView);
                StartViewPolling(_currentView);
            }

            // 상태바 업데이트
            _statusBar.Items[0].Title = $"~F1~ Dashboard{(viewName == "Dashboard" ? "*" : "")}";
            _statusBar.Items[1].Title = $"~F2~ Users{(viewName == "Users" ? "*" : "")}";
            _statusBar.Items[2].Title = $"~F3~ Game{(viewName == "Game" ? "*" : "")}";
            _statusBar.Items[3].Title = $"~F4~ Command{(viewName == "Command" ? "*" : "")}";
            _statusBar.Items[4].Title = $"~F5~ TrainingEnd{(viewName == "TrainingEnd" ? "*" : "")}";

            SetNeedsDisplay();
        }

        private void RefreshCurrentView()
        {
            if (_currentView is IRefreshable refreshable)
            {
                refreshable.Refresh();
            }
        }

        private void StartViewPolling(View view)
        {
            if (view is IRefreshable refreshable)
            {
                refreshable.StartPolling();
            }
        }

        private void StopViewPolling(View view)
        {
            if (view is IRefreshable refreshable)
            {
                refreshable.StopPolling();
            }
        }

        private void ShowChangeHostDialog()
        {
            var dialog = new Dialog("Change Server Host", 50, 10);
            
            var label = new Label("Server URL:")
            {
                X = Pos.Center(),
                Y = 1
            };
            
            var urlInput = new TextField(_client.BaseUrl)
            {
                X = Pos.Center(),
                Y = 2,
                Width = Dim.Percent(80)
            };
            
            var btnOk = new Button("OK", is_default: true);
            var btnCancel = new Button("Cancel");
            
            btnOk.Clicked += () =>
            {
                var newUrl = urlInput.Text.ToString();
                if (!string.IsNullOrWhiteSpace(newUrl))
                {
                    if (!newUrl.StartsWith("http://") && !newUrl.StartsWith("https://"))
                    {
                        newUrl = "http://" + newUrl;
                    }

                    _client.BaseUrl = newUrl;
                    try 
                    {
                        Console.Title = $"DarkRift Server TUI - {_client.BaseUrl}";
                    } 
                    catch { /* Ignore if not supported on platform */ }
                }
                Application.RequestStop();
            };
            
            btnCancel.Clicked += () => Application.RequestStop();
            
            dialog.AddButton(btnOk);
            dialog.AddButton(btnCancel);
            dialog.Add(label, urlInput);
            
            Application.Run(dialog);
            
            RefreshCurrentView();
        }
    }

    /// <summary>
    /// 자동 새로고침 가능한 뷰 인터페이스
    /// </summary>
    public interface IRefreshable
    {
        void Refresh();
        void StartPolling();
        void StopPolling();
    }
}
