using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FubarFTP
{
    /// <summary>
    /// CLI 커맨드 처리기
    /// </summary>
    public class CommandHandler
    {
        private readonly FtpServerManager _server;
        private readonly Dictionary<string, Func<string[], Task>> _commands;

        public CommandHandler(FtpServerManager server)
        {
            _server = server;
            _commands = new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
            {
                { "start", CmdStart },
                { "stop", CmdStop },
                { "restart", CmdRestart },
                { "status", CmdStatus },
                { "mkdir", CmdMkdir },
                { "ls", CmdList },
                { "config", CmdConfig },
                { "user", CmdUser },
                { "help", CmdHelp },
                { "quit", CmdQuit },
                { "exit", CmdQuit },
                { "clear", CmdClear },
            };
        }

        /// <summary>
        /// CLI 루프 실행
        /// </summary>
        public async Task RunAsync()
        {
            Console.WriteLine("FubarFTP Server CLI — Type 'help' for available commands.");
            Console.WriteLine();

            while (true)
            {
                Console.Write("ftp> ");
                string input = Console.ReadLine();

                if (input == null) // Ctrl+C / EOF
                    break;

                input = input.Trim();
                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = ParseArgs(input);
                string cmd = parts[0];
                string[] args = parts.Skip(1).ToArray();

                if (_commands.TryGetValue(cmd, out var handler))
                {
                    try
                    {
                        await handler(args);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown command: '{cmd}'. Type 'help' for available commands.");
                }
            }
        }

        private async Task CmdStart(string[] args)
        {
            if (!_server.IsRunning)
                await _server.StartAsync();
        }

        private async Task CmdStop(string[] args)
        {
            if (_server.IsRunning)
                await _server.StopAsync();
        }

        private async Task CmdRestart(string[] args)
        {
            await CmdStop(args);
            await Task.Delay(500);
            await CmdStart(args);
        }

        private Task CmdStatus(string[] args)
        {
            var status = _server.GetStatus();

            Console.WriteLine("=== FTP Server Status ===");
            Console.WriteLine($"  Running:     {(status.IsRunning ? "YES" : "NO")}");
            Console.WriteLine($"  Address:     {status.ServerAddress}:{status.Port}");
            Console.WriteLine($"  Root:        {status.RootPath}");
            Console.WriteLine($"  PASV Ports:  {_server.Config.PasvMinPort}-{_server.Config.PasvMaxPort}");
            Console.WriteLine($"  PASV IP:     {(string.IsNullOrEmpty(_server.Config.PasvPublicAddress) ? "(auto)" : _server.Config.PasvPublicAddress)}");
            Console.WriteLine($"  Auth:        {(_server.Config.AllowAnonymous ? "Anonymous (open)" : $"Users ({_server.Config.Users.Count} registered)")}");
            Console.WriteLine($"  Files:       {status.TotalFiles}");
            Console.WriteLine($"  Folders:     {status.TotalFolders}");
            Console.WriteLine($"  Total Size:  {FormatSize(status.TotalSizeBytes)}");
            Console.WriteLine("=========================");

            return Task.CompletedTask;
        }

        private Task CmdMkdir(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: mkdir <folder_path>");
                Console.WriteLine("  Example: mkdir training/session01");
                return Task.CompletedTask;
            }

            _server.CreateSubFolder(args[0]);
            return Task.CompletedTask;
        }

        private Task CmdList(string[] args)
        {
            string path = args.Length > 0 ? args[0] : "";
            var items = _server.ListFolders(path);

            if (items.Count == 0)
            {
                Console.WriteLine("  (empty)");
                return Task.CompletedTask;
            }

            string displayPath = string.IsNullOrEmpty(path) ? "/" : $"/{path}";
            Console.WriteLine($"  Listing: {displayPath}");
            Console.WriteLine($"  {"Type",-6} {"Size",10}  {"Name"}");
            Console.WriteLine($"  {"----",-6} {"----",10}  {"----"}");

            foreach (var item in items)
            {
                if (item.IsFile)
                {
                    Console.WriteLine($"  {"FILE",-6} {FormatSize(item.FileSize),10}  {item.Name}");
                }
                else
                {
                    string info = $"{item.FileCount}f/{item.SubFolderCount}d";
                    Console.WriteLine($"  {"DIR",-6} {info,10}  {item.Name}/");
                }
            }

            return Task.CompletedTask;
        }

        private async Task CmdConfig(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("=== Current Configuration ===");
                Console.WriteLine($"  port       = {_server.Config.Port}");
                Console.WriteLine($"  root       = {_server.Config.RootPath}");
                Console.WriteLine($"  address    = {_server.Config.ServerAddress}");
                Console.WriteLine($"  pasv-min   = {_server.Config.PasvMinPort}");
                Console.WriteLine($"  pasv-max   = {_server.Config.PasvMaxPort}");
                Console.WriteLine($"  pasv-ip    = {(string.IsNullOrEmpty(_server.Config.PasvPublicAddress) ? "(auto)" : _server.Config.PasvPublicAddress)}");
                Console.WriteLine($"  max-conn   = {_server.Config.MaxConnections}");
                Console.WriteLine($"  anon       = {(_server.Config.AllowAnonymous ? "on" : "off")}");
                Console.WriteLine($"  users      = {_server.Config.Users.Count} registered");
                Console.WriteLine($"  config     = {_server.Config.ConfigPath}");
                Console.WriteLine("=============================");
                Console.WriteLine("Usage: config <key> <value>  (auto-saves, auto-restarts if running)");
                return;
            }

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: config <key> <value>");
                return;
            }

            bool wasRunning = _server.IsRunning;
            if (wasRunning)
            {
                Console.WriteLine("[CONFIG] Stopping server to apply changes...");
                await CmdStop(Array.Empty<string>());
            }

            string key = args[0].ToLowerInvariant();
            string value = args[1];

            switch (key)
            {
                case "port":
                    if (int.TryParse(value, out int port))
                        _server.Config.Port = port;
                    else
                        Console.WriteLine("Invalid port number.");
                    break;
                case "root":
                    _server.Config.RootPath = value;
                    break;
                case "address":
                    _server.Config.ServerAddress = value;
                    break;
                case "pasv-min":
                    if (int.TryParse(value, out int pmin))
                        _server.Config.PasvMinPort = pmin;
                    break;
                case "pasv-max":
                    if (int.TryParse(value, out int pmax))
                        _server.Config.PasvMaxPort = pmax;
                    break;
                case "max-conn":
                    if (int.TryParse(value, out int mc))
                        _server.Config.MaxConnections = mc;
                    break;
                case "pasv-ip":
                    _server.Config.PasvPublicAddress = value;
                    break;
                default:
                    Console.WriteLine($"Unknown config key: '{key}'");
                    return;
            }

            Console.WriteLine($"  {key} = {value}");

            // 자동 저장
            _server.Config.Save();

            // 서버가 실행 중이었으면 자동 재시작
            if (wasRunning)
            {
                Console.WriteLine("[CONFIG] Restarting server with new settings...");
                await Task.Delay(300);
                await CmdStart(Array.Empty<string>());
            }
        }

        private async Task CmdUser(string[] args)
        {
            if (args.Length == 0)
            {
                // 현재 사용자 목록 표시
                Console.WriteLine("=== FTP Users ===");
                Console.WriteLine($"  Anonymous: {(_server.Config.AllowAnonymous ? "ALLOWED" : "DENIED")}");

                if (_server.Config.Users.Count == 0)
                {
                    Console.WriteLine("  (no registered users)");
                }
                else
                {
                    foreach (var u in _server.Config.Users)
                        Console.WriteLine($"  - {u.Username}");
                }

                Console.WriteLine("=================");
                Console.WriteLine("Usage:");
                Console.WriteLine("  user add <name> <pass>   Add user");
                Console.WriteLine("  user remove <name>       Remove user");
                Console.WriteLine("  user anon on|off         Allow/deny anonymous");
                return;
            }

            string sub = args[0].ToLowerInvariant();

            switch (sub)
            {
                case "add":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: user add <username> <password>");
                        return;
                    }

                    string addName = args[1];
                    string addPass = args[2];

                    // 중복 확인
                    bool exists = false;
                    foreach (var u in _server.Config.Users)
                    {
                        if (string.Equals(u.Username, addName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                    {
                        Console.WriteLine($"[USER] '{addName}' already exists. Remove first to change password.");
                        return;
                    }

                    _server.Config.Users.Add(new FtpUser { Username = addName, Password = addPass });
                    _server.Config.Save();
                    Console.WriteLine($"[USER] Added: {addName}");

                    // 인증 모드로 자동 전환 안내
                    if (_server.Config.AllowAnonymous)
                        Console.WriteLine("[USER] Note: Anonymous is still ON. Use 'user anon off' to require login.");

                    if (_server.IsRunning)
                    {
                        Console.WriteLine("[USER] Restarting server to apply...");
                        await CmdRestart(Array.Empty<string>());
                    }
                    break;

                case "remove":
                case "rm":
                case "del":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: user remove <username>");
                        return;
                    }

                    string rmName = args[1];
                    int removed = _server.Config.Users.RemoveAll(
                        u => string.Equals(u.Username, rmName, StringComparison.OrdinalIgnoreCase));

                    if (removed > 0)
                    {
                        _server.Config.Save();
                        Console.WriteLine($"[USER] Removed: {rmName}");

                        if (_server.IsRunning)
                        {
                            Console.WriteLine("[USER] Restarting server to apply...");
                            await CmdRestart(Array.Empty<string>());
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[USER] '{rmName}' not found.");
                    }
                    break;

                case "anon":
                case "anonymous":
                    if (args.Length < 2)
                    {
                        Console.WriteLine($"  Anonymous: {(_server.Config.AllowAnonymous ? "ON" : "OFF")}");
                        Console.WriteLine("Usage: user anon on|off");
                        return;
                    }

                    string toggle = args[1].ToLowerInvariant();
                    if (toggle == "on" || toggle == "true" || toggle == "1")
                    {
                        _server.Config.AllowAnonymous = true;
                        Console.WriteLine("[USER] Anonymous access: ON");
                    }
                    else if (toggle == "off" || toggle == "false" || toggle == "0")
                    {
                        if (_server.Config.Users.Count == 0)
                        {
                            Console.WriteLine("[USER] WARNING: No users registered! Add a user first or nobody can connect.");
                            Console.WriteLine("  Example: user add admin 1234");
                        }

                        _server.Config.AllowAnonymous = false;
                        Console.WriteLine("[USER] Anonymous access: OFF");
                    }
                    else
                    {
                        Console.WriteLine("Usage: user anon on|off");
                        return;
                    }

                    _server.Config.Save();

                    if (_server.IsRunning)
                    {
                        Console.WriteLine("[USER] Restarting server to apply...");
                        await CmdRestart(Array.Empty<string>());
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown user command: '{sub}'");
                    Console.WriteLine("Usage: user add|remove|anon");
                    break;
            }
        }

        private Task CmdHelp(string[] args)
        {
            Console.WriteLine("=== FubarFTP Commands ===");
            Console.WriteLine("  start              Start FTP server");
            Console.WriteLine("  stop               Stop FTP server");
            Console.WriteLine("  restart            Restart FTP server");
            Console.WriteLine("  status             Show server status");
            Console.WriteLine("  mkdir <path>       Create sub-folder (e.g. mkdir training/day1)");
            Console.WriteLine("  ls [path]          List files and folders");
            Console.WriteLine("  config             Show current config (saved to ftpconfig.json)");
            Console.WriteLine("  config <key> <val> Change config (auto-saves, auto-restarts)");
            Console.WriteLine("  user               Show registered users");
            Console.WriteLine("  user add <n> <p>   Add user with password");
            Console.WriteLine("  user remove <n>    Remove user");
            Console.WriteLine("  user anon on|off   Toggle anonymous access");
            Console.WriteLine("  clear              Clear console");
            Console.WriteLine("  help               Show this help");
            Console.WriteLine("  quit / exit        Shutdown and exit");
            Console.WriteLine("=========================");
            return Task.CompletedTask;
        }

        private Task CmdQuit(string[] args)
        {
            Console.WriteLine("Shutting down...");
            Environment.Exit(0);
            return Task.CompletedTask;
        }

        private Task CmdClear(string[] args)
        {
            Console.Clear();
            return Task.CompletedTask;
        }

        private static List<string> ParseArgs(string input)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                }
                else if (c == ' ' && !inQuote)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
