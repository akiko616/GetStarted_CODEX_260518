using System;
using BCrypt.Net;

namespace DatabasePlugin
{
    /// <summary>
    /// 비밀번호 해싱 및 검증 유틸리티
    /// BCrypt 알고리즘 사용
    /// </summary>
    public static class PasswordHasher
    {
        // Cost factor (높을수록 안전하지만 느림, 권장: 12)
        private const int WorkFactor = 12;

        /// <summary>
        /// 비밀번호 해시 생성
        /// </summary>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password cannot be empty", nameof(password));
            }

            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        /// <summary>
        /// 비밀번호 검증
        /// </summary>
        public static bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (SaltParseException)
            {
                // 잘못된 해시 형식
                return false;
            }
        }

        /// <summary>
        /// 비밀번호 강도 검증
        /// </summary>
        public static bool IsPasswordStrong(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return false;

            // 최소 8자, 최대 100자
            if (password.Length < 8 || password.Length > 100)
                return false;

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
            }

            // 대문자, 소문자, 숫자 포함
            return hasUpper && hasLower && hasDigit;
        }
    }
}
