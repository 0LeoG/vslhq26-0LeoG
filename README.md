# onboard-me

A codebase onboarding assistant that helps developers understand an unfamiliar repository with chat, repo mapping, and cited answers.

## Team

- **Team name (or "Solo"):** Solo
- **Members:**
  - LeoG (@0LeoG)

## Category

- **Primary:** Copilot integration
- **Secondary (optional):** Best AI Agent or Workflow Automation

## What it does

onboard-me is a web app for developers who need to get productive in a codebase they have never seen before. A user connects a GitHub repository, the app ingests the repository structure and selected files, and then builds an onboarding workspace with chat, a repo overview, and task-focused guidance.

The goal is to reduce the time it takes to answer common onboarding questions like where to start, which files matter for a feature, and how major parts of the system fit together. Answers are grounded in retrieved repository context and should point the user back to the relevant files.

## Architecture

The planned architecture is an ASP.NET Core / Blazor web app with server-side Azure OpenAI integration and a RAG pipeline over repository content.

```text
GitHub repo
   |
   v
Repo ingestion service
   |
   +--> metadata + repo map store
   |
   +--> chunking pipeline --> embeddings --> vector index
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

Main planned components:

- **Blazor web UI** for repo submission, chat, and repo overview
- **ASP.NET Core backend services** for GitHub ingestion, indexing, and orchestration
- **Chunking and retrieval pipeline** for code and documentation
- **Azure OpenAI** for embeddings and chat completions
- **Vector index** for semantic search over repository chunks
- **Metadata store** for repo state, indexing status, and workspace data

## Tech stack

- **Languages:** C#, SQL
- **Frameworks/libraries:** ASP.NET Core, Blazor
- **AI models/services:** Azure OpenAI for embeddings and chat completions
- **Hosting:** TBD

## Solution structure

```text
onboard-me.sln
src/
  OnboardMe.Web/        # ASP.NET Core Blazor app
```

## Getting started

### Prerequisites

- .NET SDK 8 or later
- A GitHub account
- GitHub OAuth app or equivalent GitHub auth setup for private repo access
- Azure OpenAI resource with:
  - a chat model deployment
  - an embeddings model deployment
- A vector-capable storage option for retrieval

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

# Optional: override the callback path if needed
dotnet user-secrets set "GitHub:CallbackPath" "/signin-github" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Optional: add Azure OpenAI settings locally
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com/" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ApiKey" "your-azure-openai-api-key" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ChatDeployment" "your-chat-deployment" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:EmbeddingsDeployment" "your-embeddings-deployment" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "AzureOpenAI:ApiVersion" "2024-02-01" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Optional: add vector index settings locally
dotnet user-secrets set "VectorIndex:Provider" "your-vector-provider" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "VectorIndex:ConnectionString" "your-vector-connection-string" --project src/OnboardMe.Web/OnboardMe.Web.csproj
dotnet user-secrets set "VectorIndex:IndexName" "your-vector-index-name" --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Run the app
dotnet run --project src/OnboardMe.Web/OnboardMe.Web.csproj

# Or force the HTTPS launch profile explicitly
dotnet run --project src/OnboardMe.Web/OnboardMe.Web.csproj --launch-profile https
```

### Configuration

The app is expected to need configuration for:

- GitHub client ID / client secret
- GitHub OAuth callback path (default: `/signin-github`)
- Azure OpenAI endpoint
- Azure OpenAI API key
- Azure OpenAI chat deployment name
- Azure OpenAI embeddings deployment name
- Vector index or search service connection settings

Do not commit secrets. Use local environment variables, .NET user secrets, or example config files that only show the expected shape.

For local development, prefer `.NET user secrets`:

- Keep `src/OnboardMe.Web/appsettings.json` free of secrets.
- Use `src/OnboardMe.Web/appsettings.Example.json` only as a shape reference.
- Store real values outside the repo with `dotnet user-secrets`.
- Inspect local secrets with `dotnet user-secrets list --project src/OnboardMe.Web/OnboardMe.Web.csproj`.

For GitHub OAuth, configure your app callback URL to `https://localhost:<port>/signin-github` (or your configured callback path).

To re-run embeddings for a previously indexed repository, call:

```bash
curl -X POST https://localhost:<port>/repos/<owner>/<repo>/embeddings/rerun
```

## Demo (required)

- **Video file in this repo (preferred):** `./demo/demo.mp4`
- **Video link (YouTube, Loom, etc.) if not committed to repo:**
- **Deployed URL (if any):**

## Known limitations

- The repository is still in the setup phase and the full application is not implemented yet.
- Private repository support depends on the final GitHub authentication flow.
- Retrieval quality will depend on chunking, metadata quality, and the vector storage approach selected during implementation.
- The repo map will likely start as a lightweight structural view before any deeper code graph analysis is added.

## License

MIT
