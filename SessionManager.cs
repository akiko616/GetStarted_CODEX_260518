using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DarkRift.Server;

namespace DatabasePlugin
{
    /// <summary>
    /// 로그인한 사용자의 세션 관리 (관리자 기능 포함)
    /// 스레드 안전한 account.id ↔ IClient 매핑
    /// 가상 유저 지원 (IClient 없이 세션만 존재)
    /// </summary>
    public class SessionManager
    {
        // account.id -> IClient 매핑
        private readonly ConcurrentDictionary<string, IClient> accountToClient = new ConcurrentDictionary<string, IClient>();

        // IClient.ID -> account.id 역매핑 (빠른 검색용)
        private readonly ConcurrentDictionary<ushort, string> clientToAccount = new ConcurrentDictionary<ushort, string>();

        // account.id -> AccountType 매핑 (관리자 필터링용)
        private readonly ConcurrentDictionary<string, int> accountTypes = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, DateTime> loginTimes = new ConcurrentDictionary<string, DateTime>();

        // 가상 유저 관리 (IClient 없이 세션만 존재, SendMessage 수신 불가)
        private readonly ConcurrentDictionary<string, int> virtualUsers = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, DateTime> virtualLoginTimes = new ConcurrentDictionary<string, DateTime>();

        // 로그 스로틀링
        private DateTime lastLogTime = DateTime.MinValue;
        private readonly TimeSpan logThrottle = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 로그인 성공 시 세션 등록 (AccountType 포함)
        /// </summary>
        public bool RegisterSession(string accountId, IClient client, int accountType = 0)
        {
            if (string.IsNullOrWhiteSpace(accountId) || client == null)
            {
                return false;
            }

            try
            {
                // 기존 세션이 있다면 제거 (중복 로그인 처리)
                if (accountToClient.TryGetValue(accountId, out IClient oldClient))
                {
                    RemoveSession(accountId);
                    LogThrottled($"[SessionManager] Duplicate login detected: {accountId}, old session removed");
                }

                // 새 세션 등록
                accountToClient[accountId] = client;
                clientToAccount[client.ID] = accountId;
                accountTypes[accountId] = accountType;
                loginTimes[accountId] = DateTime.Now;

                string roleText = accountType == (int)AccountTypeEnum.Admin ? " (ADMIN)" : "";
                Console.WriteLine($"[SessionManager] Session registered: {accountId}{roleText} (ClientID: {client.ID})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager ERROR] RegisterSession failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 세션 제거 (로그아웃 또는 연결 끊김)
        /// </summary>
        public bool RemoveSession(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            try
            {
                if (accountToClient.TryRemove(accountId, out IClient client))
                {
                    clientToAccount.TryRemove(client.ID, out _);
                    accountTypes.TryRemove(accountId, out _);
                    loginTimes.TryRemove(accountId, out _);
                    Console.WriteLine($"[SessionManager] Session removed: {accountId}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager ERROR] RemoveSession failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 클라이언트 연결 끊김 시 세션 제거
        /// </summary>
        public bool RemoveSessionByClient(ushort clientId)
        {
            try
            {
                if (clientToAccount.TryRemove(clientId, out string accountId))
                {
                    accountToClient.TryRemove(accountId, out _);
                    accountTypes.TryRemove(accountId, out _);
                    loginTimes.TryRemove(accountId, out _);
                    Console.WriteLine($"[SessionManager] Session removed by client disconnect: {accountId} (ClientID: {clientId})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager ERROR] RemoveSessionByClient failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// account.id로 IClient 조회
        /// </summary>
        public IClient GetClient(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return null;
            }

            accountToClient.TryGetValue(accountId, out IClient client);
            return client;
        }

        /// <summary>
        /// IClient로 account.id 조회
        /// </summary>
        public string GetAccountId(IClient client)
        {
            if (client == null)
            {
                return null;
            }

            clientToAccount.TryGetValue(client.ID, out string accountId);
            return accountId;
        }

        /// <summary>
        /// account.id로 계정 타입 조회 (가상 유저 포함)
        /// </summary>
        public int GetAccountType(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return 0;
            }

            if (accountTypes.TryGetValue(accountId, out int accountType))
            {
                return accountType;
            }

            // 가상 유저 확인
            if (virtualUsers.TryGetValue(accountId, out int virtualType))
            {
                return virtualType;
            }

            return 0;
        }

        /// <summary>
        /// IClient로 계정 타입 조회
        /// </summary>
        public int GetAccountType(IClient client)
        {
            string accountId = GetAccountId(client);
            return GetAccountType(accountId);
        }

        /// <summary>
        /// 관리자 여부 확인
        /// </summary>
        public bool IsAdmin(string accountId)
        {
            return GetAccountType(accountId) == (int)AccountTypeEnum.Admin;
        }

        /// <summary>
        /// 관리자 여부 확인 (IClient)
        /// </summary>
        public bool IsAdmin(IClient client)
        {
            return GetAccountType(client) == (int)AccountTypeEnum.Admin;
        }

        /// <summary>
        /// 모든 관리자 클라이언트 목록 반환
        /// </summary>
        public IReadOnlyList<IClient> GetAdminClients()
        {
            var adminClients = new List<IClient>();

            foreach (var kvp in accountTypes)
            {
                if (kvp.Value == (int)AccountTypeEnum.Admin)
                {
                    if (accountToClient.TryGetValue(kvp.Key, out IClient client))
                    {
                        adminClients.Add(client);
                    }
                }
            }

            return adminClients;
        }

        /// <summary>
        /// 모든 관리자 계정 ID 목록 반환
        /// </summary>
        public IReadOnlyList<string> GetAdminAccountIds()
        {
            return accountTypes
                .Where(kvp => kvp.Value == (int)AccountTypeEnum.Admin)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// 세션 존재 여부 확인 (가상 유저 포함)
        /// </summary>
        public bool HasSession(string accountId)
        {
            return !string.IsNullOrWhiteSpace(accountId) &&
                   (accountToClient.ContainsKey(accountId) || virtualUsers.ContainsKey(accountId));
        }

        /// <summary>
        /// 전체 로그인된 클라이언트 목록 반환
        /// </summary>
        public IReadOnlyList<IClient> GetAllClients()
        {
            return accountToClient.Values.ToList();
        }

        /// <summary>
        /// 전체 로그인된 계정 ID 목록 반환
        /// </summary>
        public IReadOnlyList<string> GetAllAccountIds()
        {
            return accountToClient.Keys.ToList();
        }

        /// <summary>
        /// 전체 접속자 세션 정보 반환 (관리자용, 가상 유저 포함)
        /// </summary>
        public List<OnlineUserInfo> GetAllOnlineUsers()
        {
            var users = new List<OnlineUserInfo>();

            // 실제 유저
            foreach (var kvp in accountToClient)
            {
                string accountId = kvp.Key;
                IClient client = kvp.Value;
                DateTime loginTime;

                accountTypes.TryGetValue(accountId, out int accountType);

                if (loginTimes.TryGetValue(accountId, out loginTime)) { }
                else
                    loginTime = DateTime.Now;

                users.Add(new OnlineUserInfo
                {
                    AccountId = accountId,
                    ClientId = client.ID,
                    AccountType = accountType,
                    LoginTime = loginTime
                });
            }

            // 가상 유저 (ClientId = 0, SendMessage 수신 불가)
            foreach (var kvp in virtualUsers)
            {
                string accountId = kvp.Key;
                int accountType = kvp.Value;
                virtualLoginTimes.TryGetValue(accountId, out DateTime loginTime);

                users.Add(new OnlineUserInfo
                {
                    AccountId = accountId,
                    ClientId = 0,
                    AccountType = accountType,
                    LoginTime = loginTime
                });
            }

            return users;
        }

        /// <summary>
        /// 특정 AccountType의 접속자만 반환
        /// </summary>
        public List<OnlineUserInfo> GetOnlineUsersByType(int accountType)
        {
            return GetAllOnlineUsers()
                .Where(u => u.AccountType == accountType)
                .ToList();
        }

        /// <summary>
        /// 현재 로그인된 사용자 수 (가상 유저 포함)
        /// </summary>
        public int SessionCount => accountToClient.Count + virtualUsers.Count;

        /// <summary>
        /// 현재 로그인된 관리자 수
        /// </summary>
        public int AdminCount => accountTypes.Count(kvp => kvp.Value == (int)AccountTypeEnum.Admin);

        /// <summary>
        /// 현재 로그인된 일반 사용자 수
        /// </summary>
        public int UserCount => accountTypes.Count(kvp => kvp.Value == (int)AccountTypeEnum.User);

        #region Virtual User Management

        /// <summary>
        /// 가상 유저 등록 (IClient 없음, SendMessage 수신 불가)
        /// TUI에서 테스트용으로 파티에 추가할 수 있는 유저
        /// </summary>
        public bool RegisterVirtualUser(string accountId, int accountType = 0)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            try
            {
                // 실제 세션이 이미 존재하면 등록 불가
                if (accountToClient.ContainsKey(accountId))
                {
                    Console.WriteLine($"[SessionManager] Virtual user denied: {accountId} already has real session");
                    return false;
                }

                // 기존 가상 유저가 있으면 덮어쓰기
                virtualUsers[accountId] = accountType;
                virtualLoginTimes[accountId] = DateTime.Now;

                string roleText = accountType == (int)AccountTypeEnum.Admin ? " (ADMIN)" : "";
                Console.WriteLine($"[SessionManager] Virtual user registered: {accountId}{roleText}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager ERROR] RegisterVirtualUser failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 가상 유저 제거
        /// </summary>
        public bool RemoveVirtualUser(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            bool removed = virtualUsers.TryRemove(accountId, out _);
            if (removed)
            {
                virtualLoginTimes.TryRemove(accountId, out _);
                Console.WriteLine($"[SessionManager] Virtual user removed: {accountId}");
            }
            return removed;
        }

        /// <summary>
        /// 가상 유저 여부 확인
        /// </summary>
        public bool IsVirtualUser(string accountId)
        {
            return !string.IsNullOrWhiteSpace(accountId) && virtualUsers.ContainsKey(accountId);
        }

        /// <summary>
        /// 가상 유저 수
        /// </summary>
        public int VirtualUserCount => virtualUsers.Count;

        #endregion

        /// <summary>
        /// 모든 세션 클리어 (서버 종료 시)
        /// </summary>
        public void ClearAllSessions()
        {
            int count = accountToClient.Count + virtualUsers.Count;
            accountToClient.Clear();
            clientToAccount.Clear();
            accountTypes.Clear();
            loginTimes.Clear();
            virtualUsers.Clear();
            virtualLoginTimes.Clear();
            Console.WriteLine($"[SessionManager] All sessions cleared: {count} sessions");
        }

        /// <summary>
        /// 로그 스로틀링
        /// </summary>
        private void LogThrottled(string message)
        {
            var now = DateTime.Now;
            if (now - lastLogTime >= logThrottle)
            {
                Console.WriteLine(message);
                lastLogTime = now;
            }
        }

        /// <summary>
        /// 세션 상태 덤프 (디버깅용)
        /// </summary>
        public void DumpSessions()
        {
            Console.WriteLine($"[SessionManager] === Session Dump ({SessionCount} active, {AdminCount} admins, {VirtualUserCount} virtual) ===");
            foreach (var kvp in accountToClient)
            {
                int accountType = GetAccountType(kvp.Key);
                string roleText = accountType == (int)AccountTypeEnum.Admin ? " [Admin]" : "";
                Console.WriteLine($"  - Account: {kvp.Key}{roleText}, ClientID: {kvp.Value.ID}");
            }
            foreach (var kvp in virtualUsers)
            {
                string roleText = kvp.Value == (int)AccountTypeEnum.Admin ? " [Admin]" : "";
                Console.WriteLine($"  - [VIRTUAL] Account: {kvp.Key}{roleText}");
            }
        }
    }
}
