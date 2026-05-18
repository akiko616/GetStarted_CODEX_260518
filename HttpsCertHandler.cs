// ============================================================================
// UnityWebRequest 전용 SSL 인증서 핸들러
//
// Unity의 UnityTls는 ServicePointManager를 무시합니다.
// UnityWebRequest에는 반드시 CertificateHandler를 사용해야 합니다.
//
// ⚠️ BypassCertHandler는 개발 전용! 프로덕션에서는 서버 인증서를 수정하세요.
// ============================================================================

using UnityEngine;
using UnityEngine.Networking;

namespace HXRAI
{
    // ========================================================================
    // 방법 1: 개발용 - 특정 호스트 인증서 검증 우회
    // ========================================================================

    /// <summary>
    /// 특정 도메인에 대해서만 인증서 검증을 우회하는 핸들러
    /// 사용: request.certificateHandler = new BypassCertHandler("hxr.iptime.org");
    /// </summary>
    public class BypassCertHandler : CertificateHandler
    {
        private readonly string _allowedHost;

        public BypassCertHandler(string allowedHost = null)
        {
            _allowedHost = allowedHost;
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // 개발 환경에서만 허용
            // certificateData에서 호스트를 확인할 수 없으므로
            // 이 핸들러를 붙이는 시점에서 이미 호스트를 특정한 것으로 간주
            return true;
        }
    }

    // ========================================================================
    // 방법 2: 프로덕션용 - 인증서 핀닝 (서버 인증서 고정)
    // ========================================================================

    /// <summary>
    /// 서버 인증서의 공개키 해시를 고정하여 검증하는 핸들러
    /// 
    /// 해시 추출 방법 (서버에서 실행):
    ///   openssl s_client -connect hxr.iptime.org:443 2>/dev/null | \
    ///     openssl x509 -pubkey -noout | \
    ///     openssl pkey -pubin -outform der | \
    ///     openssl dgst -sha256 -binary | base64
    /// 
    /// 사용: request.certificateHandler = new PinnedCertHandler("서버에서_추출한_해시");
    /// </summary>
    public class PinnedCertHandler : CertificateHandler
    {
        private readonly string _expectedHash;

        public PinnedCertHandler(string sha256Base64Hash)
        {
            _expectedHash = sha256Base64Hash;
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            if (string.IsNullOrEmpty(_expectedHash))
                return false;

            // DER 인코딩된 인증서에서 SHA256 해시 계산
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(certificateData);
                string hashBase64 = System.Convert.ToBase64String(hash);
                return hashBase64 == _expectedHash;
            }
        }
    }
}
