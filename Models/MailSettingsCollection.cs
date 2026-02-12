using System.Collections.ObjectModel;

namespace MailTrayNotifier.Models
{
    /// <summary>
    /// 다중 메일 계정 설정 컬렉션
    /// </summary>
    public sealed class MailSettingsCollection
    {
        /// <summary>
        /// 새로고침 기능 사용 여부 (기본값: true)
        /// </summary>
        public bool IsRefreshEnabled { get; set; } = true;

        /// <summary>
        /// UI 언어 코드 (빈 문자열 = 시스템 기본)
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// 메일 계정 목록
        /// </summary>
        public List<MailSettings> Accounts { get; set; } = new();

        /// <summary>
        /// 유효한 계정 개수 (필수 값이 모두 입력된 계정)
        /// </summary>
        public int ValidAccountCount() => Accounts.Count(a => a.HasRequiredValues());
    }
}
