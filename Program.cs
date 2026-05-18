using System;
using System.Linq;
using Terminal.Gui;

namespace ServerTUI
{
    class Program
    {
        static void Main(string[] args)
        {
            // 인자 파싱
            string host = "http://localhost:8080";

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--host" || args[i] == "-h") && i + 1 < args.Length)
                {
                    host = args[i + 1];
                    i++;
                }
            }

            // 호스트 URL 정규화
            if (!host.StartsWith("http://") && !host.StartsWith("https://"))
            {
                host = "http://" + host;
            }
            host = host.TrimEnd('/');

            Console.Title = $"DarkRift Server TUI - {host}";

            // 연결 테스트
            var client = new ApiClient(host);
            Console.WriteLine($"Connecting to {host}...");

            try
            {
                var status = client.GetServerStatusAsync().Result;
                if (status != null && status.Success)
                {
                    Console.WriteLine($"Connected! Sessions: {status.Data.ActiveSessions}, Uptime: {status.Data.UptimeSeconds:F0}s");
                }
                else
                {
                    Console.WriteLine($"Warning: Server responded but status check failed. Continuing anyway...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not connect to server ({ex.InnerException?.Message ?? ex.Message})");
                Console.WriteLine("TUI will start anyway. Press any key to continue...");
                Console.ReadKey(true);
            }

            // TUI 시작
            Application.Init();

            try
            {
                var mainWindow = new Views.MainWindow(client, host);
                Application.Run(mainWindow);
            }
            finally
            {
                Application.Shutdown();
            }
        }
    }
}
