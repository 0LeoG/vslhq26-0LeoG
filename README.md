# onboard-me

onboard-me is a demo-ready onboarding assistant for unfamiliar codebases. The app helps a developer sign in to GitHub, ingest a repository, and then ask grounded questions about that codebase with citations back to the source files.

## Team

- **Team name (or "Solo"):** Solo
- **Members:**
  - LeoG (@0LeoG)

## Category

- **Primary:** Copilot integration
- **Secondary (optional):** Best AI Agent or Workflow Automation

## What it does

The current app includes a working end-to-end flow for repository onboarding:

- Sign in with GitHub and connect a repository URL
- Ingest repository content in the background while tracking progress
- Build a repository overview with tracked files, language breakdowns, and notable files
- Ask questions in a chat experience that uses retrieved repository context and returns citations
- Generate lightweight “start here” suggestions for a task prompt to help developers get oriented quickly

The experience is designed to reduce the time it takes to answer questions like “where should I start?”, “which files matter for this feature?”, and “how does this system fit together?”

## Current implementation

The project is currently implemented as an ASP.NET Core Blazor web app with Azure OpenAI-backed repository assistance.

### Key capabilities

- **GitHub OAuth sign-in** for authenticated repo setup
- **Repository ingestion pipeline** that fetches repository content, chunks it, and prepares embeddings
- **Background indexing status** with progress updates and per-repository state
- **Repository overview page** for architecture summaries and structural inspection
- **Chat experience** with retrieval-based answers, conversation history, and source citations
- **In-memory demo storage** for indexing state, embeddings, and conversations

## Architecture

```text
GitHub repository
   |
   v
Repository ingestion service
   |
   +--> metadata + indexing status
   |
   +--> chunking pipeline --> embeddings --> vector search store
                                         |
User question ---------------------------+
                                         |
                                         v
                           retrieval + prompt assembly
                                         |
                                         v
                                  Azure OpenAI answer
                                         |
                                         v
                           Blazor chat + repo overview UI
```

## Tech stack

- **Languages:** C#
- **Frameworks/libraries:** ASP.NET Core, Blazor Server, Markdig
- **AI services:** Azure OpenAI for embeddings and chat completions
- **External APIs:** GitHub API and GitHub OAuth

## Solution structure

```text
onboard-me.sln
src/
  OnboardMe.Web/               # ASP.NET Core Blazor app
  OnboardMe.Web.Models/        # Shared models for ingestion and chat flows
tests/
  OnboardMe.Web.Tests/         # Unit and integration-style tests
```

## Getting started

### Prerequisites

- .NET SDK 10 or later
- A GitHub account
- A GitHub OAuth app for sign-in
- An Azure OpenAI resource with:
  - a chat deployment
  - an embeddings deployment

### Setup

```bash
# Clone the repo
git clone https://github.com/0LeoG/vslhq26-0LeoG.git
cd vslhq26-0LeoG

# Restore dependencies
dotnet restore onboard-me.sln

# Initialize local user secrets for the web project
dotnet user-secrets init --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Set GitHub OAuth secrets locally
dotnet user-secrets set "GitHub:ClientId" "your-github-client-id" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "GitHub:ClientSecret" "your-github-client-secret" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "GitHub:CallbackPath" "/signin-github" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Add Azure OpenAI settings locally
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com/" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ApiKey" "your-azure-openai-api-key" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ChatDeployment" "your-chat-deployment" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:EmbeddingsDeployment" "your-embeddings-deployment" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ApiVersion" "2024-02-01" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Optional: add a GitHub personal access token if you hit rate limits
dotnet user-secrets set "GitHub:AccessToken" "your-github-personal-access-token" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Run the app
dotnet run --project src/OnboardMe.Web/OnboardMe.Web.csproj
```

### Configuration

The app expects configuration for:

- GitHub client ID and client secret
- GitHub OAuth callback path (default: `/signin-github`)
- Optional GitHub personal access token
- Azure OpenAI endpoint, API key, chat deployment, and embeddings deployment

Do not commit secrets. Prefer local environment variables or .NET user secrets.

For local development, inspect or manage secrets with:

```bash
dotnet user-secrets list --project src/OnboardMe.Web/OnboardMe.Web.csproj
```

### Main app routes

- `/repo-setup` — connect and index a repository
- `/repo-overview` — inspect repository summaries and structure
- `/chat` — ask questions and receive cited answers

## Testing

Run the test suite with:

```bash
dotnet test onboard-me.sln
```

## Demo

- **Video file in this repo (preferred):** `./demo/demo.mp4`
- **Video link (YouTube, Loom, etc.) if not committed to repo:**
- **Deployed URL (if any):**

## Current limitations

- Repository support is currently focused on root GitHub repository URLs
- Private repository access is not yet enabled in this pass
- The current implementation uses in-memory storage, which is suitable for demos but not production-scale persistence
- Retrieval quality will continue to improve as chunking, prompts, and embeddings evolve

## License

MIT
