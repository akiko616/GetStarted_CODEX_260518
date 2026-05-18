using System;
using System.IO;
using System.Linq;

namespace FubarFTP
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  FubarFTP Server v1.1.0");
            Console.WriteLine("  Based on FubarDev.FtpServer 3.1.2");
            Console.WriteLine("  noweel with Antigravity");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // 설정 로드: 저장된 설정 파일 → CLI 인자로 오버라이드
            var config = FtpConfig.Load();
            ApplyArgs(config, args);

            using (var server = new FtpServerManager(config))
            {
                // --autostart 옵션이 있으면 바로 시작
                bool autoStart = args.Any(a =>
                    a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-a", StringComparison.OrdinalIgnoreCase));

                if (autoStart)
                {
                    server.StartAsync().GetAwaiter().GetResult();
                }

                // CLI 루프 실행
                var cli = new CommandHandler(server);
                cli.RunAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// 커맨드라인 인자 파싱
        /// --port 2121 --root C:\FtpData --api-port 8021 --autostart
        /// </summary>
        private static void ApplyArgs(FtpConfig config, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                string next = (i + 1 < args.Length) ? args[i + 1] : null;

                switch (arg)
                {
                    case "--port":
                    case "-p":
                        if (next != null && int.TryParse(next, out int port))
                        {
                            config.Port = port;
                            i++;
                        }
                        break;

                    case "--root":
                    case "-r":
                        if (next != null)
                        {
                            config.RootPath = Path.GetFullPath(next);
                            i++;
                        }
                        break;

                    case "--address":
                        if (next != null)
                        {
                            config.ServerAddress = next;
                            i++;
                        }
                        break;

                    case "--pasv-min":
                        if (next != null && int.TryParse(next, out int pmin))
                        {
                            config.PasvMinPort = pmin;
                            i++;
                        }
                        break;

                    case "--pasv-max":
                        if (next != null && int.TryParse(next, out int pmax))
                        {
                            config.PasvMaxPort = pmax;
                            i++;
                        }
                        break;

                    case "--max-conn":
                        if (next != null && int.TryParse(next, out int mc))
                        {
                            config.MaxConnections = mc;
                            i++;
                        }
                        break;

                    case "--autostart":
                    case "-a":
                        // autostart는 Main에서 처리
                        break;

                    case "--help":
                    case "-h":
                        PrintUsage();
                        Environment.Exit(0);
                        break;
                }
            }

        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: FubarFTP [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --port, -p <port>       FTP port (default: 21)");
            Console.WriteLine("  --root, -r <path>       Root folder path (default: ./FtpRoot)");
            Console.WriteLine("  --address <addr>        Bind address (default: 0.0.0.0)");
            Console.WriteLine("  --pasv-min <port>       PASV min port (default: 49152)");
            Console.WriteLine("  --pasv-max <port>       PASV max port (default: 49200)");
            Console.WriteLine("  --max-conn <n>          Max connections (default: 20)");
            Console.WriteLine("  --autostart, -a         Auto-start server on launch");
            Console.WriteLine("  --help, -h              Show this help");
        }
    }
}
