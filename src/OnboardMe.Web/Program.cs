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
builder.Services.AddSingleton<IAzureOpenAiEmbeddingService, AzureOpenAiEmbeddingService>();
builder.Services.AddSingleton<IAzureOpenAiChatService, AzureOpenAiChatService>();
builder.Services.AddSingleton<IRepositoryIngestionService, RepositoryIngestionService>();

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

app.MapPost("/repos/{owner}/{repository}/embeddings/rerun", async (
    string owner,
    string repository,
    IRepositoryIngestionService repositoryIngestionService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var embeddedChunks = await repositoryIngestionService.RegenerateEmbeddingsAsync(owner, repository, cancellationToken);
        return Results.Ok(new { owner, repository, embeddedChunks });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

// POST /repos/{owner}/{repository}/search
// Body: { "query": "user question", "topK": 5 }
// Converts the question to an embedding and returns the most relevant chunks for that repo.
app.MapPost("/repos/{owner}/{repository}/search", async (
    string owner,
    string repository,
    SearchRequest body,
    IAzureOpenAiEmbeddingService embeddingService,
    IRepositoryEmbeddingStore embeddingStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(body.Query))
    {
        return Results.BadRequest(new { message = "Query must not be empty." });
    }

    var topK = body.TopK is > 0 ? body.TopK.Value : 5;

    // Embed the query as a single-chunk synthetic record so we can reuse the embedding service.
    var queryChunk = new OnboardMe.Web.Services.RepoIngestion.RepositoryContentChunk
    {
        ChunkId = "query:0",
        SourcePath = "__query__",
        SourceSha = string.Empty,
        ChunkIndex = 0,
        Strategy = "query",
        StartLine = 0,
        EndLine = 0,
        Content = body.Query
    };

    IReadOnlyList<OnboardMe.Web.Services.RepoIngestion.RepositoryChunkEmbeddingRecord> queryEmbeddings;
    try
    {
        queryEmbeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, [queryChunk], cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Embedding generation failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var queryEmbedding = queryEmbeddings[0].Embedding;
    var results = await embeddingStore.SearchByEmbeddingAsync(owner, repository, queryEmbedding, topK, cancellationToken);

    return Results.Ok(new
    {
        owner,
        repository,
        query = body.Query,
        results = results.Select(r => new
        {
            chunkId = r.Chunk.ChunkId,
            sourcePath = r.Chunk.SourcePath,
            startLine = r.Chunk.StartLine,
            endLine = r.Chunk.EndLine,
            score = r.Score,
            content = r.Chunk.Content
        })
    });
});

// POST /repos/{owner}/{repository}/chat
// Body: { "question": "...", "topK": 5 }
// Retrieves the most relevant chunks, sends them to Azure OpenAI Chat, and returns an answer with file citations.
app.MapPost("/repos/{owner}/{repository}/chat", async (
    string owner,
    string repository,
    ChatRequest body,
    IAzureOpenAiEmbeddingService embeddingService,
    IRepositoryEmbeddingStore embeddingStore,
    IAzureOpenAiChatService chatService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(body.Question))
    {
        return Results.BadRequest(new { message = "Question must not be empty." });
    }

    var topK = body.TopK is > 0 ? body.TopK.Value : 5;

    // Step 1: embed the question so we can retrieve relevant chunks.
    var queryChunk = new OnboardMe.Web.Services.RepoIngestion.RepositoryContentChunk
    {
        ChunkId = "query:0",
        SourcePath = "__query__",
        SourceSha = string.Empty,
        ChunkIndex = 0,
        Strategy = "query",
        StartLine = 0,
        EndLine = 0,
        Content = body.Question
    };

    IReadOnlyList<OnboardMe.Web.Services.RepoIngestion.RepositoryChunkEmbeddingRecord> queryEmbeddings;
    try
    {
        queryEmbeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, [queryChunk], cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Embedding generation failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    // Step 2: retrieve the most relevant chunks from the store.
    var queryEmbedding = queryEmbeddings[0].Embedding;
    var contextChunks = await embeddingStore.SearchByEmbeddingAsync(owner, repository, queryEmbedding, topK, cancellationToken);

    // Step 3: send question + context to the chat model and return a grounded answer.
    OnboardMe.Web.Services.RepoIngestion.ChatAnswer chatAnswer;
    try
    {
        chatAnswer = await chatService.AnswerAsync(owner, repository, body.Question, contextChunks, cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Chat completion failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    return Results.Ok(new
    {
        owner,
        repository,
        question = body.Question,
        answer = chatAnswer.Answer,
        citations = chatAnswer.Citations.Select(c => new
        {
            path = c.Path,
            startLine = c.StartLine,
            endLine = c.EndLine
        })
    });
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

/// <summary>Request body for the semantic search endpoint.</summary>
internal sealed class SearchRequest
{
    /// <summary>The natural-language question to search for.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Maximum number of results to return. Defaults to 5 when omitted or ≤ 0.</summary>
    public int? TopK { get; init; }
}

/// <summary>Request body for the chat endpoint.</summary>
internal sealed class ChatRequest
{
    /// <summary>The natural-language question to answer.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>Maximum number of context chunks to retrieve. Defaults to 5 when omitted or ≤ 0.</summary>
    public int? TopK { get; init; }
}
