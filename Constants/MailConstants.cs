namespace MailTrayNotifier.Constants
{
    /// <summary>
    /// 메일 관련 상수값을 중앙화한 클래스
    /// </summary>
    public static class MailConstants
    {
        /// <summary>
        /// 기본 POP3 포트 (SSL)
        /// </summary>
        public const int DefaultPop3Port = 995;

        /// <summary>
        /// 기본 SMTP 포트 (STARTTLS)
        /// </summary>
        public const int DefaultSmtpPort = 587;

        /// <summary>
        /// 메일 확인 기간 (일)
        /// </summary>
        public const int MaxDaysToCheck = 30;

        /// <summary>
        /// 연결 타임아웃 (초)
        /// </summary>
        public const int ConnectionTimeoutSeconds = 30;

        /// <summary>
        /// 최대 메일 가져오기 건수
        /// </summary>
        public const int MaxMailsToFetch = 100;

        /// <summary>
        /// 연속 오래된 메일 임계값
        /// </summary>
        public const int ConsecutiveOldMailThreshold = 10;

        /// <summary>
        /// 최대 계정 수
        /// </summary>
        public const int MaxAccounts = 10;

        /// <summary>
        /// 기본 새로고침 주기 (분)
        /// </summary>
        public const int DefaultRefreshMinutes = 5;

        /// <summary>
        /// 계정별 락 대기 시간 (초)
        /// </summary>
        public const int AccountLockTimeoutSeconds = 30;

        /// <summary>
        /// 연결 해제(QUIT) 타임아웃 (초)
        /// </summary>
        public const int DisconnectTimeoutSeconds = 3;

        /// <summary>
        /// 영구 오류 시 최대 시도 횟수. 이 횟수만큼 폴링 주기마다 재시도한 뒤 해당 계정을 중지한다.
        /// 일시적 네트워크 오류는 이 제한에 포함되지 않고 제한 없이 재시도한다.
        /// </summary>
        public const int MaxPermanentErrorAttempts = 3;
    }
}
