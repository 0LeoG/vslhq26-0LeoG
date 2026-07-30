using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace OnboardMe.Web.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IConfiguration configuration) : ControllerBase
{
    private const string GitHubScheme = "GitHub";
    private const string GitHubClientIdConfigKey = "GitHub:ClientId";
    private const string GitHubClientSecretConfigKey = "GitHub:ClientSecret";

    [HttpGet("signin")]
    public IActionResult SignIn([FromQuery] string? returnUrl)
    {
        var clientId = configuration[GitHubClientIdConfigKey];
        var clientSecret = configuration[GitHubClientSecretConfigKey];
        var githubAuthConfigured = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);

        if (!githubAuthConfigured)
        {
            return Problem(
                title: "GitHub authentication is not configured.",
                detail: $"Set {GitHubClientIdConfigKey} and {GitHubClientSecretConfigKey} to enable sign-in.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var redirectUri = NormalizeReturnUrl(returnUrl);
        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            [GitHubScheme]);
    }

    [HttpGet("signout")]
    public IActionResult SignOut([FromQuery] string? returnUrl)
    {
        var redirectUri = NormalizeReturnUrl(returnUrl);
        return SignOut(
            new AuthenticationProperties { RedirectUri = redirectUri },
            [CookieAuthenticationDefaults.AuthenticationScheme]);
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/repo-setup";
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
        {
            return "/repo-setup";
        }

        if (!returnUrl.StartsWith('/'))
        {
            return "/repo-setup";
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/repo-setup";
        }

        if (returnUrl.Contains('\\'))
        {
            return "/repo-setup";
        }

        return returnUrl;
    }
}
