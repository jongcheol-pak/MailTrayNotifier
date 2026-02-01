namespace MailTrayNotifier.Models
{
    /// <summary>
    /// 메일 UID 상태 저장용 파일 모델
    /// </summary>
    public sealed class MailStateFile
    {
        /// <summary>
        /// 계정별 UID 목록 (순서 보장을 위해 List 사용)
        /// </summary>
        public Dictionary<string, List<string>> Accounts { get; set; } = new();
    }
}
