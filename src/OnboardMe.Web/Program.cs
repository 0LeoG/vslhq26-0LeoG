using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using OnboardMe.Web.Components;
using OnboardMe.Web.Services.RepoIngestion;

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
builder.Services.AddControllers();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(RepositoryIngestionService.GitHubApiClientName, client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppUserAgentProductName, AppUserAgentProductVersion));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
});
builder.Services.AddHttpClient(AzureOpenAiEmbeddingService.AzureOpenAiClientName);
builder.Services.AddHttpClient(AzureOpenAiChatService.AzureOpenAiChatClientName);
builder.Services.Configure<AzureOpenAiEmbeddingsOptions>(builder.Configuration.GetSection(AzureOpenAiEmbeddingsOptions.SectionName));
builder.Services.AddSingleton<IRepositoryIndexingStatusStore, InMemoryRepositoryIndexingStatusStore>();
builder.Services.AddSingleton<IRepositoryEmbeddingStore, InMemoryRepositoryEmbeddingStore>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSingleton<IAzureOpenAiEmbeddingService, AzureOpenAiEmbeddingService>();
builder.Services.AddSingleton<IAzureOpenAiChatService, AzureOpenAiChatService>();
builder.Services.AddSingleton<IRepositoryOverviewAiService, AzureOpenAiRepositoryOverviewService>();
builder.Services.AddSingleton<IRepositoryIngestionService, RepositoryIngestionService>();

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie();

AddGitHubOAuth(authentication, builder.Configuration);

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

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// --- Helper: GitHub OAuth setup ---

/// <summary>
/// Registers GitHub OAuth with the authentication builder when credentials are present in configuration.
/// If <c>GitHub:ClientId</c> or <c>GitHub:ClientSecret</c> are absent the method is a no-op.
/// </summary>
static void AddGitHubOAuth(AuthenticationBuilder auth, IConfiguration configuration)
{
    var clientId = configuration[GitHubClientIdConfigKey];
    var clientSecret = configuration[GitHubClientSecretConfigKey];
    var callbackPath = configuration[GitHubCallbackPathConfigKey] ?? DefaultGitHubCallbackPath;

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        return;
    }

    auth.AddOAuth(GitHubScheme, options =>
    {
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        options.CallbackPath = callbackPath;
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.SaveTokens = true;

        options.Scope.Add("read:user");

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

