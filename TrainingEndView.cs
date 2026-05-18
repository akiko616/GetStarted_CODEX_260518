using System;
using System.Collections.Generic;
using Terminal.Gui;

namespace ServerTUI.Views
{
    /// <summary>
    /// Training End 뷰 — TrainResultPush / GracefulEnd 수동 트리거
    /// </summary>
    public class TrainingEndView : FrameView, IRefreshable
    {
        private readonly ApiClient _client;

        // 공통 파티 ID 입력
        private readonly TextField _partyIdField;

        // 출력 로그
        private readonly FrameView _logFrame;
        private readonly ListView _logList;
        private readonly List<string> _logItems = new List<string>();

        public TrainingEndView(ApiClient client) : base("Training End Control")
        {
            _client = client;

            // ── 파티 ID 행 ──
            var lblParty = new Label("Party ID:")
            {
                X = 1, Y = 1
            };
            _partyIdField = new TextField("")
            {
                X = Pos.Right(lblParty) + 1, Y = 1,
                Width = 10
            };

            // ── TrainResultPush 섹션 ──
            var pushFrame = new FrameView("TrainResultPush (TAG 237 직접 배포)")
            {
                X = 1, Y = 3,
                Width = Dim.Fill(1),
                Height = 5
            };

            var btnPush = new Button("▶  TrainResultPush 전송")
            {
                X = 1, Y = 1
            };
            btnPush.Clicked += OnTrainResultPushClicked;

            pushFrame.Add(btnPush);

            // ── GracefulEnd 섹션 ──
            var endFrame = new FrameView("GracefulEnd (TAG 238 훈련생 배포)")
            {
                X = 1, Y = Pos.Bottom(pushFrame) + 1,
                Width = Dim.Fill(1),
                Height = 6
            };

            var lblEndDesc = new Label("파티 훈련생 전원에게 GracefulEnd 전송. 클라이언트 ACK 수집 후 자동으로 GracefulQuit → 데디케이트 전송됩니다.")
            {
                X = 1, Y = 1,
                Width = Dim.Fill(2)
            };

            var btnEnd = new Button("▶  GracefulEnd 전송")
            {
                X = 1, Y = 3
            };
            btnEnd.Clicked += OnGracefulEndClicked;

            endFrame.Add(lblEndDesc, btnEnd);

            // ── 로그 영역 ──
            _logFrame = new FrameView("Output")
            {
                X = 1,
                Y = Pos.Bottom(endFrame) + 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(1)
            };

            _logList = new ListView(_logItems)
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _logFrame.Add(_logList);

            Add(lblParty, _partyIdField, pushFrame, endFrame, _logFrame);

            AppendLog("[System] Party ID를 입력하고 원하는 동작을 실행하세요.");
        }

        private int GetPartyId()
        {
            var text = _partyIdField.Text?.ToString()?.Trim();
            if (int.TryParse(text, out int partyId) && partyId > 0)
                return partyId;
            return -1;
        }

        private void OnTrainResultPushClicked()
        {
            int partyId = GetPartyId();
            if (partyId < 0)
            {
                AppendLog("[Error] 유효한 Party ID를 입력하세요.");
                return;
            }

            AppendLog($"> TrainResultPush 전송 중... (Party={partyId})");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.TrainResultPushAsync(partyId);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true)
                        AppendLog($"  [OK] {resp.Data?.Message}");
                    else
                        AppendLog($"  [Error] {resp?.Error ?? "Failed"}");
                });
            });
        }

        private void OnGracefulEndClicked()
        {
            int partyId = GetPartyId();
            if (partyId < 0)
            {
                AppendLog("[Error] 유효한 Party ID를 입력하세요.");
                return;
            }

            AppendLog($"> GracefulEnd 전송 중... (Party={partyId})");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var resp = await _client.GracefulEndAsync(partyId);
                Application.MainLoop.Invoke(() =>
                {
                    if (resp?.Success == true)
                        AppendLog($"  [OK] {resp.Data?.Message}");
                    else
                        AppendLog($"  [Error] {resp?.Error ?? "Failed"}");
                });
            });
        }

        private void AppendLog(string line)
        {
            _logItems.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            _logList.SetSource(_logItems);
            if (_logItems.Count > 0)
                _logList.SelectedItem = _logItems.Count - 1;
            SetNeedsDisplay();
        }

        public void Refresh() { }
        public void StartPolling() { _partyIdField.SetFocus(); }
        public void StopPolling() { }
    }
}
