using System;
using System.Data;
using System.Data.SQLite;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace DatabasePlugin
{
    /// <summary>
    /// ìŠ¤ë ˆë“œ ì•ˆì „í•œ SQLite ë°ì´í„°ë² ì´ìŠ¤ ê´€ë¦¬ í´ëž˜ìŠ¤
    /// ì—°ê²° í’€ë§ íŒ¨í„´ ì‚¬ìš©
    /// </summary>
    public class SQLiteManager : IDisposable
    {
        private readonly string _connectionString;
        private bool _disposed = false;
        
        // ë¡œê·¸ ìŠ¤ë¡œí‹€ë§ (í‚¤ë³„ ê°œë³„ ê´€ë¦¬)
        private readonly ConcurrentDictionary<string, DateTime> _lastLogTime = new ConcurrentDictionary<string,DateTime>();
        private readonly TimeSpan _logThrottle = TimeSpan.FromSeconds(5);
        
        // ìž¬ì‹œë„ ì„¤ì •
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 100;

        public SQLiteManager(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                throw new ArgumentException("Database path cannot be empty", nameof(dbPath));
            }

            _connectionString = $"Data Source={dbPath};Version=3;Pooling=True;Max Pool Size=100;";
        }

        /// <summary>
        /// DB ì—°ê²° ìƒì„± (ë§¤ë²ˆ ìƒˆë¡œìš´ ì—°ê²° ë°˜í™˜)
        /// </summary>
        private SQLiteConnection CreateConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// DB ì´ˆê¸°í™” ë° í…Œì´ë¸” ìƒì„±
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    // WAL ëª¨ë“œ í™œì„±í™”
                    using (var cmd = new SQLiteCommand("PRAGMA journal_mode=WAL;", connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // ì™¸ëž˜í‚¤ í™œì„±í™”
                    using (var cmd = new SQLiteCommand("PRAGMA foreign_keys=ON;", connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Account í…Œì´ë¸” ìƒì„±
                    await CreateTableIfNotExistsAsync(connection);
                }

                Console.WriteLine("[SQLiteManager] Database initialized successfully.");
                Console.WriteLine($"[SQLiteManager] Connection string: {_connectionString}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SQLiteManager FATAL] Initialize failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Account í…Œì´ë¸” ìƒì„±
        /// </summary>
        private async Task CreateTableIfNotExistsAsync(SQLiteConnection connection)
        {
            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS account (
                    id TEXT PRIMARY KEY NOT NULL,
                    password_hash TEXT NOT NULL,
                    account_index INTEGER NOT NULL DEFAULT 0,
                    account_type INTEGER NOT NULL DEFAULT 0,
                    user_info TEXT NOT NULL DEFAULT '',
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE INDEX IF NOT EXISTS idx_account_id ON account(id);
                CREATE INDEX IF NOT EXISTS idx_account_type ON account(account_type);
                
                CREATE TRIGGER IF NOT EXISTS update_account_timestamp 
                AFTER UPDATE ON account
                BEGIN
                    UPDATE account SET updated_at = CURRENT_TIMESTAMP WHERE id = NEW.id;
                END;

                CREATE TABLE IF NOT EXISTS train_result (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    party_id INTEGER NOT NULL,
                    training_type TEXT NOT NULL,
                    execution_time TEXT NOT NULL,
                    result_json TEXT NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE INDEX IF NOT EXISTS idx_train_result_party_id ON train_result(party_id);
                CREATE INDEX IF NOT EXISTS idx_train_result_training_type ON train_result(training_type);
            ";

            using (var cmd = new SQLiteCommand(createTableQuery, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            Console.WriteLine("[SQLiteManager] Database tables created/verified.");
        }

        #region CREATE

        /// <summary>
        /// 훈련 결과 저장
        /// </summary>
        public async Task<bool> SaveTrainResultAsync(int partyId, string trainingType, string executionTime, string resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
                return false;

            for (int retry = 0; retry < MaxRetries; retry++)
            {
                try
                {
                    using (var connection = CreateConnection())
                    {
                        string query = @"
                            INSERT INTO train_result (party_id, training_type, execution_time, result_json)
                            VALUES (@partyId, @trainingType, @executionTime, @resultJson)
                        ";

                        using (var cmd = new SQLiteCommand(query, connection))
                        {
                            cmd.Parameters.AddWithValue("@partyId", partyId);
                            cmd.Parameters.AddWithValue("@trainingType", trainingType ?? string.Empty);
                            cmd.Parameters.AddWithValue("@executionTime", executionTime ?? string.Empty);
                            cmd.Parameters.AddWithValue("@resultJson", resultJson);

                            int rowsAffected = await cmd.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                Console.WriteLine($"[DB] TrainResult saved for Party: {partyId}");
                                return true;
                            }
                        }
                    }
                }
                catch (SQLiteException ex) when (ex.ErrorCode == (int)SQLiteErrorCode.Busy && retry < MaxRetries - 1)
                {
                    LogThrottled($"[DB] Database busy, retrying... ({retry + 1}/{MaxRetries})", "savetrain_busy");
                    await Task.Delay(RetryDelayMs * (retry + 1));
                    continue;
                }
                catch (Exception ex)
                {
                    LogThrottled($"[DB ERROR] SaveTrainResult failed: {ex.Message}", "save_trainresult_error");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// ê³„ì • ìƒ ì„± (ìž¬ì‹œë „ ë¡œì§  í ¬í•¨)
        /// </summary>
        public async Task<bool> CreateAccountAsync(AccountData account)
        {
            if (account == null || !account.IsValid())
            {
                LogThrottled("[DB] Invalid account data", "create_invalid");
                return false;
            }

            for (int retry = 0; retry < MaxRetries; retry++)
            {
                try
                {
                    using (var connection = CreateConnection())
                    {
                        string query = @"
                            INSERT INTO account (id, password_hash, account_index, account_type, user_info)
                            VALUES (@id, @passwordHash, @accountIndex, @accountType, @userInfo)
                        ";

                        using (var cmd = new SQLiteCommand(query, connection))
                        {
                            cmd.Parameters.AddWithValue("@id", account.Id);
                            cmd.Parameters.AddWithValue("@passwordHash", account.PasswordHash);
                            cmd.Parameters.AddWithValue("@accountIndex", account.AccountIndex);
                            cmd.Parameters.AddWithValue("@accountType", account.AccountType);
                            cmd.Parameters.AddWithValue("@userInfo", account.UserInfo ?? string.Empty);

                            int rowsAffected = await cmd.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                Console.WriteLine($"[DB] Account created: {account.Id}");
                                return true;
                            }
                        }
                    }
                }
                catch (SQLiteException ex) when (ex.ErrorCode == (int)SQLiteErrorCode.Constraint)
                {
                    LogThrottled($"[DB] Account already exists: {account.Id}", "create_duplicate");
                    return false;  // ìž¬ì‹œë„ ë¶ˆí•„ìš”
                }
                catch (SQLiteException ex) when (ex.ErrorCode == (int)SQLiteErrorCode.Busy && retry < MaxRetries - 1)
                {
                    LogThrottled($"[DB] Database busy, retrying... ({retry + 1}/{MaxRetries})", "create_busy");
                    await Task.Delay(RetryDelayMs * (retry + 1));
                    continue;
                }
                catch (SQLiteException ex)
                {
                    LogThrottled($"[DB ERROR] CreateAccount SQLite error: {ex.ErrorCode} - {ex.Message}", "create_sqlite_error");
                    return false;
                }
                catch (Exception ex)
                {
                    LogThrottled($"[DB FATAL] CreateAccount unexpected error: {ex.Message}", "create_fatal");
                    throw;
                }
            }

            return false;
        }

        #endregion

        #region READ

        public async Task<List<AccountData>> GetAllAcountsTypeOfAsync(int type)
        {
            var accounts = new List<AccountData>();

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = @"
                        SELECT id, password_hash, account_index, account_type, user_info, created_at
                        FROM account
                        WHERE account_type = @accountType
                    ";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@accountType", type);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                accounts.Add(new AccountData(
                                    reader.GetString(0),
                                    reader.GetString(1),
                                    reader.GetInt32(2),
                                    reader.GetInt32(3),
                                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                                    reader.GetDateTime(5)
                                ));
                            }
                        }
                    }
                }

                Console.WriteLine($"[DB] Loaded {accounts.Count} accounts with type {type}");
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] GetAllAccountsTypeOf failed: {ex.Message}", $"gettype_{type}_error");
            }

            return accounts;
        }


        /// <summary>
        /// IDë¡œ ê³„ì • ì¡°íšŒ
        /// </summary>
        public async Task<AccountData> GetAccountByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = @"
                        SELECT id, password_hash, account_index, account_type, user_info, created_at
                        FROM account
                        WHERE id = @id
                        LIMIT 1
                    ";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new AccountData(
                                    reader.GetString(0),
                                    reader.GetString(1),
                                    reader.GetInt32(2),
                                    reader.GetInt32(3),
                                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                                    reader.GetDateTime(5)
                                );
                            }
                        }
                    }
                }
            }
            catch (SQLiteException ex)
            {
                LogThrottled($"[DB ERROR] GetAccountById SQLite error: {ex.Message}", "read_error");
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB FATAL] GetAccountById unexpected error: {ex.Message}", "read_fatal");
                throw;
            }

            return null;
        }

        /// <summary>
        /// IDì™€ ë¹„ë°€ë²ˆí˜¸ë¡œ ê³„ì • ì¸ì¦
        /// </summary>
        public async Task<AccountData> AuthenticateAsync(string id, string password)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            try
            {
                // 1ë‹¨ê³„: ê³„ì • ì¡°íšŒ
                AccountData account = await GetAccountByIdAsync(id);
                if (account == null)
                {
                    LogThrottled($"[DB] Account not found: {id}", "auth_notfound");
                    return null;
                }

                // 2ë‹¨ê³„: ë¹„ë°€ë²ˆí˜¸ ê²€ì¦
                bool isValid = PasswordHasher.VerifyPassword(password, account.PasswordHash);
                if (!isValid)
                {
                    LogThrottled($"[DB] Invalid password: {id}", "auth_invalid");
                    return null;
                }

                Console.WriteLine($"[DB] Authentication success: {id}");
                return account;
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] Authenticate failed: {ex.Message}", "auth_error");
                return null;
            }
        }

        /// <summary>
        /// ëª¨ë“  ê³„ì • ì¡°íšŒ (ê´€ë¦¬ìš©)
        /// </summary>
        public async Task<List<AccountData>> GetAllAccountsAsync(int limit = 100)
        {
            var accounts = new List<AccountData>();

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = @"
                        SELECT id, password_hash, account_index, account_type, user_info, created_at
                        FROM account
                        LIMIT @limit
                    ";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@limit", limit);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                accounts.Add(new AccountData(
                                    reader.GetString(0),
                                    reader.GetString(1),
                                    reader.GetInt32(2),
                                    reader.GetInt32(3),
                                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                                    reader.GetDateTime(5)
                                ));
                            }
                        }
                    }
                }

                Console.WriteLine($"[DB] Loaded {accounts.Count} accounts");
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] GetAllAccounts failed: {ex.Message}", "getall_error");
            }

            return accounts;
        }
        #endregion

        #region UPDATE

        /// <summary>
        /// ê³„ì • ì •ë³´ ì—…ë°ì´íŠ¸
        /// </summary>
        public async Task<bool> UpdateAccountAsync(AccountData account)
        {
            if (account == null || !account.IsValid())
            {
                return false;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    // password_hash는 ChangePassword API에서만 변경 — 여기서는 제외
                    string query = @"
                        UPDATE account
                        SET account_index = @accountIndex,
                            account_type = @accountType,
                            user_info = @userInfo
                        WHERE id = @id
                    ";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", account.Id);
                        cmd.Parameters.AddWithValue("@accountIndex", account.AccountIndex);
                        cmd.Parameters.AddWithValue("@accountType", account.AccountType);
                        cmd.Parameters.AddWithValue("@userInfo", account.UserInfo ?? string.Empty);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            Console.WriteLine($"[DB] Account updated: {account.Id}");
                            return true;
                        }
                        else
                        {
                            LogThrottled($"[DB] Account not found for update: {account.Id}", "update_notfound");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] UpdateAccount failed: {ex.Message}", "update_error");
            }

            return false;
        }

        /// <summary>
        /// ë¹„ë°€ë²ˆí˜¸ë§Œ ì—…ë°ì´íŠ¸
        /// </summary>
        public async Task<bool> UpdatePasswordAsync(string id, string newPasswordHash)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(newPasswordHash))
            {
                return false;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = "UPDATE account SET password_hash = @passwordHash WHERE id = @id";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@passwordHash", newPasswordHash);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            Console.WriteLine($"[DB] Password updated: {id}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] UpdatePassword failed: {ex.Message}", "updatepw_error");
            }

            return false;
        }

        #endregion

        #region DELETE

        /// <summary>
        /// ê³„ì • ì‚­ì œ
        /// </summary>
        public async Task<bool> DeleteAccountAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = "DELETE FROM account WHERE id = @id";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            Console.WriteLine($"[DB] Account deleted: {id}");
                            return true;
                        }
                        else
                        {
                            LogThrottled($"[DB] Account not found for deletion: {id}", "delete_notfound");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] DeleteAccount failed: {ex.Message}", "delete_error");
            }

            return false;
        }

        #endregion

        #region UTILITY

        /// <summary>
        /// ê³„ì • ì¡´ìž¬ ì—¬ë¶€ í™•ì¸
        /// </summary>
        public async Task<bool> AccountExistsAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    string query = "SELECT COUNT(*) FROM account WHERE id = @id";

                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);

                        long count = (long)await cmd.ExecuteScalarAsync();
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"[DB ERROR] AccountExists check failed: {ex.Message}", "exists_error");
                return false;
            }
        }

        /// <summary>
        /// ë¡œê·¸ ìŠ¤ë¡œí‹€ë§ (í‚¤ë³„ ê°œë³„ ê´€ë¦¬)
        /// </summary>
        private void LogThrottled(string message, string key)
        {
            var now = DateTime.Now;

            if (_lastLogTime.TryGetValue(key, out var lastTime))
            {
                if (now - lastTime < _logThrottle)
                {
                    return;  // ìŠ¤ë¡œí‹€ë§
                }
            }

            Console.WriteLine(message);
            _lastLogTime[key] = now;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // ê´€ë¦¬ë˜ëŠ” ë¦¬ì†ŒìŠ¤ ì •ë¦¬
                _lastLogTime.Clear();
                Console.WriteLine("[SQLiteManager] Disposed.");
            }

            _disposed = true;
        }

        ~SQLiteManager()
        {
            Dispose(false);
        }

        #endregion
    }
}
