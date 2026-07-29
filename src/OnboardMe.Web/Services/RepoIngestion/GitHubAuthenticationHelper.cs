using System.Net.Http.Headers;

namespace OnboardMe.Web.Services.RepoIngestion;

public static class GitHubAuthenticationHelper
{
    public static void ApplyAuthorization(HttpRequestMessage request, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
