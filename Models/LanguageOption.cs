namespace MailTrayNotifier.Models;

/// <summary>
/// 언어 선택 옵션
/// </summary>
/// <param name="Code">언어 코드 (빈 문자열 = 시스템 기본)</param>
/// <param name="DisplayName">표시 이름</param>
public record LanguageOption(string Code, string DisplayName);
