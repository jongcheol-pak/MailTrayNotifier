namespace MailTrayNotifier.Models
{
    /// <summary>
    /// 메일 서버 설정 정보
    /// </summary>
    public sealed class MailSettings
    {
        /// <summary>
        /// 새로고침 기능 사용 여부 (기본값: true)
        /// </summary>
        public bool IsRefreshEnabled { get; set; } = true;

        /// <summary>
        /// POP3 서버 주소
        /// </summary>
        public string Pop3Server { get; set; } = string.Empty;

        /// <summary>
        /// POP3 포트 (기본값: 995)
        /// </summary>
        public int Pop3Port { get; set; } = 995;

        /// <summary>
        /// SMTP 서버 주소
        /// </summary>
        public string SmtpServer { get; set; } = string.Empty;

        /// <summary>
        /// SMTP 포트 (기본값: 587)
        /// </summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// SSL/TLS 사용 여부 (기본값: true)
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// 사용자 ID
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// 비밀번호 (로컬 설정에 평문 저장됨)
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 새로고침 시간(분)
        /// </summary>
        public int RefreshMinutes { get; set; } = 5;

        /// <summary>
        /// 이메일 웹사이트 주소 (선택, 알림 클릭 시 열림)
        /// </summary>
        public string MailWebUrl { get; set; } = string.Empty;

        /// <summary>
        /// 필수 설정 입력 여부
        /// </summary>
        public bool HasRequiredValues()
        {
            return !string.IsNullOrWhiteSpace(Pop3Server)
                && !string.IsNullOrWhiteSpace(UserId)
                && !string.IsNullOrWhiteSpace(Password)
                && RefreshMinutes > 0;
        }

        /// <summary>
        /// 계정 구분 키
        /// </summary>
        public string GetAccountKey()
        {
            return $"{Pop3Server}|{UserId}";
        }
    }
}
