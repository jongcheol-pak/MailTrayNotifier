namespace MailTrayNotifier.Models;

/// <summary>
/// 테마 선택 옵션
/// </summary>
/// <param name="Code">테마 코드 (빈 문자열 = 시스템 기본, "dark", "light")</param>
/// <param name="DisplayName">표시 이름</param>
public record ThemeOption(string Code, string DisplayName);
