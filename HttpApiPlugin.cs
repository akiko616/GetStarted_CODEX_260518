using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DarkRift;
using DarkRift.Server;
using DrkRft_Shared.Api;
using GameInstancePlugin;
using Newtonsoft.Json;
using PartyManagerPlugin;

namespace DatabasePlugin
{
    /// <summary>
    /// HTTP REST API 플러그인 - 서버 관리용
    /// 원격 CLI 및 TUI에서 서버를 제어하기 위한 엔드포인트 제공
    /// </summary>
    public class HttpApiPlugin : Plugin
    {
        public override Version Version => new Version(2, 0, 0);
        public override bool ThreadSafe => true;

        private HttpListener _listener;
        private bool _isRunning;
        private int _port = 8080;
        private readonly DateTime _startTime;

        // JSON 직렬화 설정
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ",
            Formatting = Formatting.None
        };

        public HttpApiPlugin(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            _startTime = DateTime.UtcNow;

            if (pluginLoadData.Settings != null && pluginLoadData.Settings["Port"] != null)
            {
                if (int.TryParse(pluginLoadData.Settings["Port"], out int port))
                {
                    _port = port;
                }
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");

            StartListener();
        }

        private void StartListener()
        {
            try
            {
                _listener.Start();
                _isRunning = true;
                Logger.Info($"HTTP API Server started on port {_port}");
                Task.Run(ListenLoop);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start HTTP listener: {ex.Message}");
            }
        }

        private async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Listen loop error: {ex.Message}");
                }
            }
        }

        #region Request Handling

        private void HandleRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            string path = request.Url.AbsolutePath.ToLower().TrimEnd('/');
            string method = request.HttpMethod.ToUpper();
            string responseString;
            int statusCode = 200;

            try
            {
                // OPTIONS (CORS preflight)
                if (method == "OPTIONS")
                {
                    statusCode = 204;
                    responseString = "";
                }
                // === Legacy Endpoints (하위 호환) ===
                else if (method == "GET" && path == "/api/status")
                {
                    responseString = HandleLegacyStatus();
                }
                else if (method == "GET" && path.StartsWith("/api/kick"))
                {
                    string accountId = request.QueryString["id"];
                    responseString = HandleLegacyKick(accountId);
                }
                else if (method == "GET" && path.StartsWith("/api/notice"))
                {
                    string msg = request.QueryString["msg"];
                    responseString = HandleLegacyNotice(msg);
                }
                else if (method == "GET" && path == "/api/help")
                {
                    responseString = HandleHelp();
                }
                // === Server ===
                else if (method == "GET" && path == "/api/server/status")
                {
                    responseString = HandleServerStatus();
                }
                // === Users ===
                else if (method == "GET" && path == "/api/users")
                {
                    responseString = HandleGetUsers();
                }
                else if (method == "GET" && path.StartsWith("/api/users/") && !path.EndsWith("/kick"))
                {
                    string accountId = ExtractPathParam(path, "/api/users/");
                    responseString = HandleGetUser(accountId);
                }
                // POST /api/users/virtual (가상 유저 등록)
                else if (method == "POST" && path == "/api/users/virtual")
                {
                    string body = ReadRequestBody(request);
                    responseString = HandleRegisterVirtualUser(body);
                }
                // POST /api/users/{accountId}/remove-virtual (가상 유저 제거)
                else if (method == "POST" && path.StartsWith("/api/users/") && path.EndsWith("/remove-virtual"))
                {
                    string accountId = ExtractMiddleParam(path, "/api/users/", "/remove-virtual");
                    responseString = HandleRemoveVirtualUser(accountId);
                }
                else if (method == "POST" && path.StartsWith("/api/users/") && path.EndsWith("/kick"))
                {
                    string accountId = ExtractMiddleParam(path, "/api/users/", "/kick");
                    responseString = HandleKickUser(accountId);
                }
                else if (method == "POST" && path == "/api/broadcast")
                {
                    string body = ReadRequestBody(request);
                    responseString = HandleBroadcast(body);
                }
                // === Parties ===
                else if (method == "GET" && path == "/api/parties")
                {
                    responseString = HandleGetParties();
                }
                else if (method == "GET" && path.StartsWith("/api/parties/") && !path.EndsWith("/disband") && !path.EndsWith("/confirm") && !path.EndsWith("/train-result-push") && !path.EndsWith("/graceful-end"))
                {
                    string idStr = ExtractPathParam(path, "/api/parties/");
                    responseString = HandleGetParty(idStr);
                }
                else if (method == "POST" && path == "/api/parties")
                {
                    string body = ReadRequestBody(request);
                    responseString = HandleCreateParty(body);
                }
                // POST /api/parties/{partyId}/add-member (파티 멤버 추가)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/add-member"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/add-member");
                    string body = ReadRequestBody(request);
                    responseString = HandleAddPartyMember(idStr, body);
                }
                // POST /api/parties/{partyId}/change-role (파티 멤버 역할 변경)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/change-role"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/change-role");
                    string body = ReadRequestBody(request);
                    responseString = HandleChangeRole(idStr, body);
                }
                // POST /api/parties/{partyId}/remove-member (파티 멤버 제거)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/remove-member"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/remove-member");
                    string body = ReadRequestBody(request);
                    responseString = HandleRemovePartyMember(idStr, body);
                }
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/disband"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/disband");
                    responseString = HandleDisbandParty(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/confirm"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/confirm");
                    responseString = HandleConfirmParty(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/request-instance"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/request-instance");
                    responseString = HandleRequestInstance(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/wait-instance"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/wait-instance");
                    responseString = HandleWaitInstance(idStr);
                }
                // POST /api/parties/{partyId}/send-wake-result (TAG 227 훈련생 전송)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/send-wake-result"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/send-wake-result");
                    responseString = HandleSendWakeResult(idStr);
                }
                // POST /api/parties/{partyId}/send-created (TAG 229 훈련생 전송)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/send-created"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/send-created");
                    responseString = HandleSendCreated(idStr);
                }
                // POST /api/parties/{partyId}/train-result-push (TAG 237 TrainResultPull 직접 배포)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/train-result-push"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/train-result-push");
                    string body = ReadRequestBody(request);
                    responseString = HandleApiTrainResultPush(idStr, body);
                }
                // POST /api/parties/{partyId}/graceful-end (TAG 238 GracefulEnd 훈련생 배포)
                else if (method == "POST" && path.StartsWith("/api/parties/") && path.EndsWith("/graceful-end"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/parties/", "/graceful-end");
                    responseString = HandleApiGracefulEnd(idStr);
                }
                // === Instances ===
                else if (method == "GET" && path == "/api/instances")
                {
                    responseString = HandleGetInstances();
                }
                else if (method == "POST" && path.StartsWith("/api/instances/") && path.EndsWith("/start"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/instances/", "/start");
                    responseString = HandleForceStartInstance(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/instances/") && path.EndsWith("/assign-party"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/instances/", "/assign-party");
                    string body = ReadRequestBody(request);
                    responseString = HandleAssignPartyToInstance(idStr, body);
                }
                else if (method == "GET" && path.StartsWith("/api/instances/") && !path.EndsWith("/stop") && !path.EndsWith("/start") && !path.EndsWith("/assign-party") && !path.EndsWith("/pause") && !path.EndsWith("/graceful-stop"))
                {
                    string idStr = ExtractPathParam(path, "/api/instances/");
                    responseString = HandleGetInstance(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/instances/") && path.EndsWith("/stop"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/instances/", "/stop");
                    responseString = HandleStopInstance(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/instances/") && path.EndsWith("/pause"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/instances/", "/pause");
                    responseString = HandleTogglePauseInstance(idStr);
                }
                else if (method == "POST" && path.StartsWith("/api/instances/") && path.EndsWith("/graceful-stop"))
                {
                    string idStr = ExtractMiddleParam(path, "/api/instances/", "/graceful-stop");
                    responseString = HandleGracefulStopInstance(idStr);
                }
                else
                {
                    statusCode = 404;
                    responseString = ToJson(ApiResponse<object>.Fail("Endpoint not found. GET /api/help for available endpoints."));
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = ToJson(ApiResponse<object>.Fail($"Internal server error: {ex.Message}"));
                Logger.Error($"Request handler error [{method} {path}]: {ex}");
            }

            SendResponse(response, responseString, statusCode);
        }

        private void SendResponse(HttpListenerResponse response, string body, int statusCode)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(body ?? "");
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json; charset=utf-8";
            response.StatusCode = statusCode;

            // CORS
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

            try
            {
                using (Stream output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send response: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private DatabaseManager GetDatabaseManager()
        {
            return PluginManager.GetPluginByType<DatabaseManager>();
        }

        private SessionManager GetSessionManager()
        {
            return GetDatabaseManager()?.GetSessionManager();
        }

        private PartyManager GetPartyManager()
        {
            return PluginManager.GetPluginByType<PartyManager>();
        }

        private GameInstanceManager GetGameInstanceManager()
        {
            return PluginManager.GetPluginByType<GameInstanceManager>();
        }

        private string ToJson(object obj)
        {
            return JsonConvert.SerializeObject(obj, JsonSettings);
        }

        private T FromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        private string ReadRequestBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody) return null;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// /api/users/{param} -> param 추출
        /// </summary>
        private string ExtractPathParam(string path, string prefix)
        {
            string remainder = path.Substring(prefix.Length);
            int slashIdx = remainder.IndexOf('/');
            return slashIdx >= 0 ? remainder.Substring(0, slashIdx) : remainder;
        }

        /// <summary>
        /// /api/users/{param}/kick -> param 추출
        /// </summary>
        private string ExtractMiddleParam(string path, string prefix, string suffix)
        {
            string remainder = path.Substring(prefix.Length);
            int suffixIdx = remainder.IndexOf(suffix);
            return suffixIdx >= 0 ? remainder.Substring(0, suffixIdx) : remainder;
        }

        private string AccountTypeToText(int accountType)
        {
            switch (accountType)
            {
                case 0: return "User";
                case 1: return "Admin";
                case 2: return "SuperAdmin";
                default: return "Unknown";
            }
        }

        #endregion

        #region Legacy Endpoints (하위 호환)

        private string HandleLegacyStatus()
        {
            var sm = GetSessionManager();
            int activeSessions = sm?.SessionCount ?? 0;
            return $"{{\"status\":\"running\",\"timestamp\":\"{DateTime.UtcNow:O}\",\"activeSessions\":{activeSessions}}}";
        }

        private string HandleLegacyKick(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return "{\"error\":\"Missing 'id' parameter.\"}";
            }

            var sm = GetSessionManager();
            if (sm != null)
            {
                sm.RemoveSession(accountId);
                return $"{{\"success\":true,\"message\":\"Kicked account: {accountId}\"}}";
            }

            return "{\"error\":\"SessionManager not available.\"}";
        }

        private string HandleLegacyNotice(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "{\"error\":\"Missing 'msg' parameter.\"}";
            }

            BroadcastNoticeToAll(message);
            return $"{{\"success\":true,\"message\":\"Notice broadcast sent.\"}}";
        }

        #endregion

        #region Server Endpoints

        private string HandleHelp()
        {
            var endpoints = new
            {
                legacy = new[]
                {
                    "GET  /api/status",
                    "GET  /api/kick?id={accountId}",
                    "GET  /api/notice?msg={message}",
                    "GET  /api/help"
                },
                server = new[]
                {
                    "GET  /api/server/status"
                },
                users = new[]
                {
                    "GET  /api/users",
                    "GET  /api/users/{accountId}",
                    "POST /api/users/{accountId}/kick",
                    "POST /api/users/virtual  {accountId, accountType}",
                    "POST /api/users/{accountId}/remove-virtual",
                    "POST /api/broadcast  {\"message\":\"...\"}"
                },
                parties = new[]
                {
                    "GET  /api/parties",
                    "GET  /api/parties/{partyId}",
                    "POST /api/parties  {partyName, sceneFile, sceneSetFile, maxPlayer, members, requestInstance}",
                    "POST /api/parties/{partyId}/add-member  {accountId, role}",
                    "POST /api/parties/{partyId}/remove-member  {accountId}",
                    "POST /api/parties/{partyId}/disband",
                    "POST /api/parties/{partyId}/confirm",
                    "POST /api/parties/{partyId}/request-instance"
                },
                instances = new[]
                {
                    "GET  /api/instances",
                    "GET  /api/instances/{id}",
                    "POST /api/instances/{id}/stop",
                    "POST /api/instances/{id}/start",
                    "POST /api/instances/{id}/assign-party  {partyId}",
                    "POST /api/instances/{id}/pause          (toggle pause/resume)",
                    "POST /api/instances/{id}/graceful-stop   (stop with signal broadcast)"
                }
            };

            return ToJson(ApiResponse<object>.Ok(endpoints));
        }

        private string HandleServerStatus()
        {
            var sm = GetSessionManager();
            var pm = GetPartyManager();
            var gim = GetGameInstanceManager();

            var allInstances = gim?.GetAllInstances();
            int activeInstances = allInstances?.Count(i => i.State != GameInstanceState.Idle) ?? 0;
            int totalInstances = allInstances?.Count ?? 0;

            var dto = new ServerStatusDto
            {
                Status = "running",
                Timestamp = DateTime.UtcNow,
                ActiveSessions = sm?.SessionCount ?? 0,
                AdminCount = sm?.AdminCount ?? 0,
                UserCount = sm?.UserCount ?? 0,
                ActiveInstances = activeInstances,
                TotalInstances = totalInstances,
                PartyCount = pm?.GetAllParties()?.Count ?? 0,
                UptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds
            };

            return ToJson(ApiResponse<ServerStatusDto>.Ok(dto));
        }

        #endregion

        #region User Endpoints

        private string HandleGetUsers()
        {
            var sm = GetSessionManager();
            if (sm == null)
            {
                return ToJson(ApiResponse<object>.Fail("SessionManager not available."));
            }

            var onlineUsers = sm.GetAllOnlineUsers();
            var dtos = onlineUsers.Select(u => new OnlineUserDto
            {
                AccountId = u.AccountId,
                ClientId = u.ClientId,
                AccountType = u.AccountType,
                AccountTypeText = AccountTypeToText(u.AccountType),
                LoginTime = u.LoginTime,
                OnlineMinutes = (DateTime.Now - u.LoginTime).TotalMinutes,
                IsVirtual = sm.IsVirtualUser(u.AccountId)
            }).ToList();

            return ToJson(ApiResponse<List<OnlineUserDto>>.Ok(dtos));
        }

        private string HandleGetUser(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return ToJson(ApiResponse<object>.Fail("Missing accountId."));
            }

            var sm = GetSessionManager();
            if (sm == null)
            {
                return ToJson(ApiResponse<object>.Fail("SessionManager not available."));
            }

            var allUsers = sm.GetAllOnlineUsers();
            var user = allUsers.FirstOrDefault(u =>
                u.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase));

            if (user == null)
            {
                return ToJson(ApiResponse<object>.Fail($"User '{accountId}' not found online."));
            }

            var dto = new OnlineUserDto
            {
                AccountId = user.AccountId,
                ClientId = user.ClientId,
                AccountType = user.AccountType,
                AccountTypeText = AccountTypeToText(user.AccountType),
                LoginTime = user.LoginTime,
                OnlineMinutes = (DateTime.Now - user.LoginTime).TotalMinutes
            };

            return ToJson(ApiResponse<OnlineUserDto>.Ok(dto));
        }

        private string HandleKickUser(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing accountId."));
            }

            var sm = GetSessionManager();
            if (sm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("SessionManager not available."));
            }

            // 연결 끊기
            var client = sm.GetClient(accountId);
            bool removed = sm.RemoveSession(accountId);

            if (client != null)
            {
                try { client.Disconnect(); } catch { }
            }

            var result = new ActionResultDto
            {
                Success = removed,
                Message = removed ? $"User '{accountId}' kicked." : $"User '{accountId}' not found."
            };

            Logger.Info($"[API] Kick user: {accountId}, result={removed}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleRegisterVirtualUser(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing request body."));
            }

            RegisterVirtualUserRequest req;
            try
            {
                req = FromJson<RegisterVirtualUserRequest>(body);
            }
            catch
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid JSON body."));
            }

            if (req == null || string.IsNullOrWhiteSpace(req.AccountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing 'accountId' in request."));
            }

            var sm = GetSessionManager();
            if (sm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("SessionManager not available."));
            }

            bool registered = sm.RegisterVirtualUser(req.AccountId, req.AccountType);

            var result = new ActionResultDto
            {
                Success = registered,
                Message = registered
                    ? $"Virtual user '{req.AccountId}' registered (type={req.AccountType})."
                    : $"Failed to register virtual user '{req.AccountId}'. May already have a real session."
            };

            Logger.Info($"[API] Register virtual user: {req.AccountId}, result={registered}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleRemoveVirtualUser(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing accountId."));
            }

            var sm = GetSessionManager();
            if (sm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("SessionManager not available."));
            }

            bool removed = sm.RemoveVirtualUser(accountId);

            var result = new ActionResultDto
            {
                Success = removed,
                Message = removed
                    ? $"Virtual user '{accountId}' removed."
                    : $"Virtual user '{accountId}' not found."
            };

            Logger.Info($"[API] Remove virtual user: {accountId}, result={removed}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleBroadcast(string body)
        {
            string message = null;

            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var req = FromJson<BroadcastRequest>(body);
                    message = req?.Message;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(message))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing 'message' in request body."));
            }

            int sent = BroadcastNoticeToAll(message);

            var result = new ActionResultDto
            {
                Success = true,
                Message = $"Notice broadcast to {sent} clients."
            };

            Logger.Info($"[API] Broadcast: \"{message}\" -> {sent} clients");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        /// <summary>
        /// 모든 접속 클라이언트에게 공지 메시지 전송
        /// </summary>
        private int BroadcastNoticeToAll(string message)
        {
            var sm = GetSessionManager();
            if (sm == null) return 0;

            var clients = sm.GetAllClients();
            int sent = 0;

            foreach (var client in clients)
            {
                try
                {
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(message);

                        using (Message msg = Message.Create(MessageTags.ServerNotice, writer))
                        {
                            client.SendMessage(msg, SendMode.Reliable);
                            sent++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Broadcast send failed to client {client.ID}: {ex.Message}");
                }
            }

            return sent;
        }

        #endregion

        #region Party Endpoints

        private string HandleGetParties()
        {
            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<object>.Fail("PartyManager not available."));
            }

            var parties = pm.GetAllParties();
            var dtos = parties.Select(p => new PartyInfoDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName,
                PartyInfo = p.PartyInfo,
                SceneFile = p.SceneFile,
                SceneSetFile = p.SceneSetFile,
                MaxPlayer = p.MaxPlayer,
                MemberCount = p.AccountIdAndRole.Count,
                Members = p.AccountIdAndRole.Select(kvp => new PartyMemberDto
                {
                    AccountId = kvp.Key,
                    Role = kvp.Value
                }).ToList()
            }).ToList();

            return ToJson(ApiResponse<List<PartyInfoDto>>.Ok(dtos));
        }

        private string HandleGetParty(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<object>.Fail("Invalid partyId."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<object>.Fail("PartyManager not available."));
            }

            var party = pm.GetPartyById(partyId);
            if (party == null)
            {
                return ToJson(ApiResponse<object>.Fail($"Party {partyId} not found."));
            }

            var dto = new PartyInfoDto
            {
                PartyId = party.PartyId,
                PartyName = party.PartyName,
                PartyInfo = party.PartyInfo,
                SceneFile = party.SceneFile,
                SceneSetFile = party.SceneSetFile,
                MaxPlayer = party.MaxPlayer,
                MemberCount = party.AccountIdAndRole.Count,
                Members = party.AccountIdAndRole.Select(kvp => new PartyMemberDto
                {
                    AccountId = kvp.Key,
                    Role = kvp.Value
                }).ToList()
            };

            return ToJson(ApiResponse<PartyInfoDto>.Ok(dto));
        }

        private string HandleCreateParty(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return ToJson(ApiResponse<object>.Fail("Missing request body."));
            }

            CreatePartyRequest req;
            try
            {
                req = FromJson<CreatePartyRequest>(body);
            }
            catch
            {
                return ToJson(ApiResponse<object>.Fail("Invalid JSON body."));
            }

            if (req == null)
            {
                return ToJson(ApiResponse<object>.Fail("Invalid request."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<object>.Fail("PartyManager not available."));
            }

            // 파티 생성
            var party = pm.CreatePartyFromApi(
                req.SceneFile ?? "",
                req.SceneSetFile ?? "",
                req.MaxPlayer > 0 ? req.MaxPlayer : 4,
                req.PartyName ?? "",
                req.PartyInfo ?? "",
                req.Members
            );

            if (party == null)
            {
                return ToJson(ApiResponse<object>.Fail("Failed to create party."));
            }

            var partyDto = new PartyInfoDto
            {
                PartyId = party.PartyId,
                PartyName = party.PartyName,
                PartyInfo = party.PartyInfo,
                SceneFile = party.SceneFile,
                SceneSetFile = party.SceneSetFile,
                MaxPlayer = party.MaxPlayer,
                MemberCount = party.AccountIdAndRole.Count,
                Members = party.AccountIdAndRole.Select(kvp => new PartyMemberDto
                {
                    AccountId = kvp.Key,
                    Role = kvp.Value
                }).ToList()
            };

            // 인스턴스 요청이 포함된 경우
            InstanceInfoDto instanceDto = null;
            string message = $"Party {party.PartyId} created.";

            if (req.RequestInstance)
            {
                var gim = GetGameInstanceManager();
                if (gim != null)
                {
                    var instance = gim.RequestInstanceForParty(party.PartyId, party.SceneFile);
                    if (instance != null)
                    {
                        instanceDto = new InstanceInfoDto
                        {
                            InstanceId = instance.InstanceId,
                            Port = instance.Port,
                            State = instance.State.ToString(),
                            AssignedPartyId = instance.AssignedPartyId,
                            StartTime = instance.StartTime == DateTime.MinValue ? (DateTime?)null : instance.StartTime,
                            IsAvailable = false
                        };
                        message += $" Instance {instance.InstanceId} (port {instance.Port}) assigned.";
                    }
                    else
                    {
                        message += " WARNING: No available instance.";
                    }
                }
                else
                {
                    message += " WARNING: GameInstanceManager not available.";
                }
            }

            var result = new CreatePartyResultDto
            {
                Party = partyDto,
                Instance = instanceDto,
                Message = message
            };

            Logger.Info($"[API] {message}");
            return ToJson(ApiResponse<CreatePartyResultDto>.Ok(result));
        }

        private string HandleRequestInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<object>.Fail("Invalid partyId."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<object>.Fail("PartyManager not available."));
            }

            var party = pm.GetPartyById(partyId);
            if (party == null)
            {
                return ToJson(ApiResponse<object>.Fail($"Party {partyId} not found."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<object>.Fail("GameInstanceManager not available."));
            }

            var instance = gim.RequestInstanceForParty(partyId, party.SceneFile);
            if (instance == null)
            {
                return ToJson(ApiResponse<object>.Fail("No available instance or already running."));
            }

            var instanceDto = new InstanceInfoDto
            {
                InstanceId = instance.InstanceId,
                Port = instance.Port,
                State = instance.State.ToString(),
                AssignedPartyId = instance.AssignedPartyId,
                StartTime = instance.StartTime == DateTime.MinValue ? (DateTime?)null : instance.StartTime,
                IsAvailable = false
            };

            Logger.Info($"[API] Instance {instance.InstanceId} requested for party {partyId}");
            return ToJson(ApiResponse<InstanceInfoDto>.Ok(instanceDto));
        }

        private string HandleWaitInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<object>.Fail("Invalid partyId."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<object>.Fail("PartyManager not available."));
            }

            var party = pm.GetPartyById(partyId);
            if (party == null)
            {
                return ToJson(ApiResponse<object>.Fail($"Party {partyId} not found."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<object>.Fail("GameInstanceManager not available."));
            }

            string error = gim.GraspInstanceForParty(partyId, party.SceneFile);
            if (error != null)
            {
                return ToJson(ApiResponse<object>.Fail(error));
            }

            return ToJson(ApiResponse<ActionResultDto>.Ok(new ActionResultDto { Message = $"Party {partyId} wait-instance reserved." }));
        }

        private string HandleAddPartyMember(string idStr, string body)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            if (string.IsNullOrEmpty(body))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing request body."));
            }

            AddPartyMemberRequest req;
            try
            {
                req = FromJson<AddPartyMemberRequest>(body);
            }
            catch
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid JSON body."));
            }

            if (req == null || string.IsNullOrWhiteSpace(req.AccountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing 'accountId' in request."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("PartyManager not available."));
            }

            bool added = pm.ForceAddMember(partyId, req.AccountId, req.Role ?? "");

            // PartyConfirm은 파티원 구성 완료 후 수동 confirm 시 전송 (POST /api/parties/{id}/confirm)

            var result = new ActionResultDto
            {
                Success = added,
                Message = added
                    ? $"Member '{req.AccountId}' added to party {partyId}."
                    : $"Failed to add '{req.AccountId}' to party {partyId}. Party not found, full, or member already exists."
            };

            Logger.Info($"[API] Add party member: party={partyId}, account={req.AccountId}, result={added}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleChangeRole(string idStr, string body)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            if (string.IsNullOrEmpty(body))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing request body."));
            }

            ChangeRoleRequest req;
            try
            {
                req = FromJson<ChangeRoleRequest>(body);
            }
            catch
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid JSON body."));
            }

            if (req == null || string.IsNullOrWhiteSpace(req.AccountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing 'accountId' in request."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("PartyManager not available."));
            }

            bool changed = pm.ForceChangeRole(partyId, req.AccountId, req.Role ?? "");

            var result = new ActionResultDto
            {
                Success = changed,
                Message = changed
                    ? $"Role of '{req.AccountId}' in party {partyId} changed to '{req.Role}'."
                    : $"Failed to change role of '{req.AccountId}' in party {partyId}. Party or member not found."
            };

            Logger.Info($"[API] Change role: party={partyId}, account={req.AccountId}, role={req.Role}, result={changed}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleRemovePartyMember(string idStr, string body)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            if (string.IsNullOrEmpty(body))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing request body."));
            }

            RemovePartyMemberRequest req;
            try
            {
                req = FromJson<RemovePartyMemberRequest>(body);
            }
            catch
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid JSON body."));
            }

            if (req == null || string.IsNullOrWhiteSpace(req.AccountId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Missing 'accountId' in request."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("PartyManager not available."));
            }

            bool removed = pm.ForceRemoveMember(partyId, req.AccountId);

            var result = new ActionResultDto
            {
                Success = removed,
                Message = removed
                    ? $"Member '{req.AccountId}' removed from party {partyId}."
                    : $"Failed to remove '{req.AccountId}' from party {partyId}. Party or member not found."
            };

            Logger.Info($"[API] Remove party member: party={partyId}, account={req.AccountId}, result={removed}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleDisbandParty(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("PartyManager not available."));
            }

            bool disbanded = pm.ForceDisbandParty(partyId);

            var result = new ActionResultDto
            {
                Success = disbanded,
                Message = disbanded ? $"Party {partyId} disbanded." : $"Party {partyId} not found."
            };

            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleConfirmParty(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            var pm = GetPartyManager();
            if (pm == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("PartyManager not available."));
            }

            var party = pm.GetPartyById(partyId);
            if (party == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail($"Party {partyId} not found."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            gim.BroadcastPartyConfirmToMembers(party);

            var result = new ActionResultDto
            {
                Success = true,
                Message = $"Party {partyId} confirmed. PartyConfirm broadcast sent to members."
            };

            Logger.Info($"[API] Party confirm: partyId={partyId}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        /// <summary>
        /// POST /api/parties/{partyId}/send-wake-result — TAG 227 훈련생 전송
        /// </summary>
        private string HandleSendWakeResult(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            var (success, message) = gim.SendWakeResultToTraineesByParty(partyId);
            var result = new ActionResultDto { Success = success, Message = message };

            Logger.Info($"[API] SendWakeResult: partyId={partyId}, success={success}");
            return success
                ? ToJson(ApiResponse<ActionResultDto>.Ok(result))
                : ToJson(ApiResponse<ActionResultDto>.Fail(message));
        }

        /// <summary>
        /// POST /api/parties/{partyId}/send-created — TAG 229 훈련생 전송
        /// </summary>
        private string HandleSendCreated(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            var (success, message) = gim.SendCreatedToTraineesByParty(partyId);
            var result = new ActionResultDto { Success = success, Message = message };

            Logger.Info($"[API] SendCreated: partyId={partyId}, success={success}");
            return success
                ? ToJson(ApiResponse<ActionResultDto>.Ok(result))
                : ToJson(ApiResponse<ActionResultDto>.Fail(message));
        }

        /// <summary>
        /// POST /api/parties/{partyId}/train-result-push — TAG 237 TrainResultPull 직접 배포 (테스트용)
        /// </summary>
        private string HandleApiTrainResultPush(string idStr, string body)
        {
            if (!int.TryParse(idStr, out int partyId))
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));

            var gim = GetGameInstanceManager();
            if (gim == null)
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));

            var req = JsonConvert.DeserializeObject<TrainResultPushApiRequest>(body ?? "{}") ?? new TrainResultPushApiRequest();

            var (success, message) = gim.ApiTriggerTrainResultPull(partyId, null);
            var result = new ActionResultDto { Success = success, Message = message };

            Logger.Info($"[API] TrainResultPush(manual): partyId={partyId}, success={success}");
            return success
                ? ToJson(ApiResponse<ActionResultDto>.Ok(result))
                : ToJson(ApiResponse<ActionResultDto>.Fail(message));
        }

        /// <summary>
        /// POST /api/parties/{partyId}/graceful-end — TAG 238 GracefulEnd 훈련생 배포
        /// </summary>
        private string HandleApiGracefulEnd(string idStr)
        {
            if (!int.TryParse(idStr, out int partyId))
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid partyId."));

            var gim = GetGameInstanceManager();
            if (gim == null)
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));

            var (success, message) = gim.ApiTriggerGracefulEnd(partyId);
            var result = new ActionResultDto { Success = success, Message = message };

            Logger.Info($"[API] GracefulEnd(manual): partyId={partyId}, success={success}");
            return success
                ? ToJson(ApiResponse<ActionResultDto>.Ok(result))
                : ToJson(ApiResponse<ActionResultDto>.Fail(message));
        }

        #endregion

        #region Instance Endpoints

        private string HandleGetInstances()
        {
            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<object>.Fail("GameInstanceManager not available."));
            }

            var instances = gim.GetAllInstances();
            var dtos = instances.Select(i => new InstanceInfoDto
            {
                InstanceId = i.InstanceId,
                Port = i.Port,
                State = i.State.ToString(),
                AssignedPartyId = i.AssignedPartyId,
                StartTime = i.StartTime == DateTime.MinValue ? (DateTime?)null : i.StartTime,
                LastHeartbeat = i.LastHeartbeat,
                IsAvailable = i.State == GameInstanceState.Idle
            }).ToList();

            return ToJson(ApiResponse<List<InstanceInfoDto>>.Ok(dtos));
        }

        private string HandleGetInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int instanceId))
            {
                return ToJson(ApiResponse<object>.Fail("Invalid instanceId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<object>.Fail("GameInstanceManager not available."));
            }

            var instance = gim.GetInstance(instanceId);
            if (instance == null)
            {
                return ToJson(ApiResponse<object>.Fail($"Instance {instanceId} not found."));
            }

            var dto = new InstanceInfoDto
            {
                InstanceId = instance.InstanceId,
                Port = instance.Port,
                State = instance.State.ToString(),
                AssignedPartyId = instance.AssignedPartyId,
                StartTime = instance.StartTime == DateTime.MinValue ? (DateTime?)null : instance.StartTime,
                LastHeartbeat = instance.LastHeartbeat,
                IsAvailable = instance.State == GameInstanceState.Idle
            };

            return ToJson(ApiResponse<InstanceInfoDto>.Ok(dto));
        }

        private string HandleStopInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int instanceId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid instanceId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            bool stopped = gim.ForceStopInstance(instanceId);

            var result = new ActionResultDto
            {
                Success = stopped,
                Message = stopped
                    ? $"Instance {instanceId} stopped."
                    : $"Instance {instanceId} not found or already idle."
            };

            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleForceStartInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int instanceId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid instanceId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            bool started = gim.ForceStartInstance(instanceId);

            var result = new ActionResultDto
            {
                Success = started,
                Message = started
                    ? $"Instance {instanceId} started."
                    : $"Instance {instanceId} not found or not in Ready state."
            };

            Logger.Info($"[API] ForceStartInstance: Instance={instanceId}, Result={started}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleTogglePauseInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int instanceId))
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid instanceId."));

            var gim = GetGameInstanceManager();
            if (gim == null)
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));

            bool toggled = gim.TogglePauseInstance(instanceId);
            var result = new ActionResultDto
            {
                Success = toggled,
                Message = toggled
                    ? $"Instance {instanceId} pause toggled."
                    : $"Instance {instanceId} not found or not in Running state."
            };

            Logger.Info($"[API] TogglePauseInstance: Instance={instanceId}, Result={toggled}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleGracefulStopInstance(string idStr)
        {
            if (!int.TryParse(idStr, out int instanceId))
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid instanceId."));

            var gim = GetGameInstanceManager();
            if (gim == null)
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));

            bool stopped = gim.GracefulStopInstance(instanceId);
            var result = new ActionResultDto
            {
                Success = stopped,
                Message = stopped
                    ? $"Instance {instanceId} graceful stop initiated."
                    : $"Instance {instanceId} not found or not in Running state."
            };

            Logger.Info($"[API] GracefulStopInstance: Instance={instanceId}, Result={stopped}");
            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        private string HandleAssignPartyToInstance(string idStr, string body)
        {
            if (!int.TryParse(idStr, out int instanceId))
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("Invalid instanceId."));
            }

            var gim = GetGameInstanceManager();
            if (gim == null)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("GameInstanceManager not available."));
            }

            var req = JsonConvert.DeserializeObject<AssignPartyRequest>(body ?? "{}");
            if (req == null || req.PartyId <= 0)
            {
                return ToJson(ApiResponse<ActionResultDto>.Fail("partyId is required and must be > 0."));
            }

            bool assigned = gim.AssignPartyToInstance(instanceId, req.PartyId);

            var result = new ActionResultDto
            {
                Success = assigned,
                Message = assigned
                    ? $"Party {req.PartyId} assigned to Instance {instanceId}."
                    : $"Failed to assign party {req.PartyId} to Instance {instanceId}. Check server logs."
            };

            Logger.Info($"[API] AssignPartyToInstance: Instance={instanceId}, Party={req.PartyId}, Result={assigned}");

            return ToJson(ApiResponse<ActionResultDto>.Ok(result));
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing && _isRunning)
            {
                _isRunning = false;
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
                Logger.Info("HTTP API Server stopped.");
            }
            base.Dispose(disposing);
        }
    }
}
