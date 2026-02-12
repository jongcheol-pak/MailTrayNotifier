using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// GitHub Releases API를 통해 최신 버전을 확인하는 서비스
    /// </summary>
    internal sealed class UpdateCheckService : IDisposable
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/jongcheol-pak/MailTrayNotifier/releases/latest";
        private readonly HttpClient _httpClient;

        public UpdateCheckService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MailTrayNotifier");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// GitHub에서 최신 릴리스 정보를 조회한다.
        /// </summary>
        /// <returns>최신 릴리스 정보. 조회 실패 시 null</returns>
        public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(GitHubApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GitHub API 응답 실패: {response.StatusCode}");
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var release = await System.Text.Json.JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
                if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    return null;
                }

                // "v1.2.3" → "1.2.3"
                var versionString = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(versionString, out var version))
                {
                    Debug.WriteLine($"버전 파싱 실패: {release.TagName}");
                    return null;
                }

                return new ReleaseInfo(version, release.HtmlUrl ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"업데이트 확인 실패: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        /// <summary>
        /// GitHub Releases API 응답 모델
        /// </summary>
        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }
        }
    }

    /// <summary>
    /// 최신 릴리스 정보
    /// </summary>
    internal sealed record ReleaseInfo(Version Version, string Url);
}
