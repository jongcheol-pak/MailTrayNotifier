namespace MailTrayNotifier.Models
{
    /// <summary>
    /// 메일 정보 모델
    /// </summary>
    public sealed record MailInfo
    {
        /// <summary>
        /// 메일 UID
        /// </summary>
        public required string Uid { get; init; }

        /// <summary>
        /// 발신자 이름
        /// </summary>
        public string SenderName { get; init; } = string.Empty;

        /// <summary>
        /// 발신자 이메일
        /// </summary>
        public string SenderEmail { get; init; } = string.Empty;

        /// <summary>
        /// 제목
        /// </summary>
        public string Subject { get; init; } = string.Empty;

        /// <summary>
        /// 발송 날짜/시간
        /// </summary>
        public DateTimeOffset Date { get; init; }

        /// <summary>
        /// 발신자 표시용 문자열
        /// </summary>
        public string SenderDisplay => string.IsNullOrWhiteSpace(SenderName) ? SenderEmail : SenderName;
    }
}
