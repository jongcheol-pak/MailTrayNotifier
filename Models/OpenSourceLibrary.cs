namespace MailTrayNotifier.Models;

/// <summary>
/// 오픈 소스 라이브러리 정보
/// </summary>
/// <param name="Name">라이브러리 이름</param>
/// <param name="License">라이선스 종류</param>
/// <param name="Url">홈페이지 URL</param>
public record OpenSourceLibrary(string Name, string License, string Url);
