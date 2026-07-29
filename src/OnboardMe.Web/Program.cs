using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using OnboardMe.Web.Components;

const string GitHubScheme = "GitHub";
const string GitHubClientIdConfigKey = "GitHub:ClientId";
const string GitHubClientSecretConfigKey = "GitHub:ClientSecret";
const string GitHubCallbackPathConfigKey = "GitHub:CallbackPath";
const string DefaultGitHubCallbackPath = "/signin-github";
const string AppUserAgentProductName = "onboard-me";
const string AppUserAgentProductVersion = "1.0";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAuthorization();

var githubClientId = builder.Configuration[GitHubClientIdConfigKey];
var githubClientSecret = builder.Configuration[GitHubClientSecretConfigKey];
var githubCallbackPath = builder.Configuration[GitHubCallbackPathConfigKey] ?? DefaultGitHubCallbackPath;
var githubAuthConfigured = !string.IsNullOrWhiteSpace(githubClientId)
    && !string.IsNullOrWhiteSpace(githubClientSecret);

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie();

if (githubAuthConfigured)
{
    authentication.AddOAuth(GitHubScheme, options =>
    {
        options.ClientId = githubClientId!;
        options.ClientSecret = githubClientSecret!;
        options.CallbackPath = githubCallbackPath;
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.SaveTokens = true;

        options.Scope.Add("read:user");
        options.Scope.Add("repo");

        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
        options.ClaimActions.MapJsonKey("urn:github:name", "name");
        options.ClaimActions.MapJsonKey("urn:github:url", "html_url");
        options.ClaimActions.MapJsonKey("urn:github:avatar_url", "avatar_url");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue(AppUserAgentProductName, AppUserAgentProductVersion));

                using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                context.RunClaimActions(user.RootElement);
            }
        };
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/signin", (HttpContext context, string? returnUrl) =>
{
    if (!githubAuthConfigured)
    {
        return Results.Problem(
            title: "GitHub authentication is not configured.",
            detail: $"Set {GitHubClientIdConfigKey} and {GitHubClientSecretConfigKey} to enable sign-in.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var redirectUri = NormalizeReturnUrl(returnUrl);
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = redirectUri },
        authenticationSchemes: [GitHubScheme]);
});

app.MapGet("/auth/signout", (string? returnUrl) =>
{
    var redirectUri = NormalizeReturnUrl(returnUrl);
    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = redirectUri },
        authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string NormalizeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/repo-setup";
    }

    if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
    {
        return "/repo-setup";
    }

    if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//") || returnUrl.StartsWith("/\\"))
    {
        return "/repo-setup";
    }

    return returnUrl;
}
