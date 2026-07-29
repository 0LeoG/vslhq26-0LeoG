---
applyTo: "**/*.{cs,csproj,props,targets,razor,json,md,yml,yaml}"
---

# .NET secrets and submission guidance

For app and config changes in this repository:

- Keep Azure OpenAI keys, GitHub tokens, and other credentials on the server side or in local development secret stores.
- Do not commit `appsettings.Development.json`, `.env`, `secrets.json`, or any file containing live credentials.
- If configuration is needed for setup, document the shape only and provide placeholder examples instead of real values.
- When adding setup or demo documentation, make sure judges can understand prerequisites, environment variables, and run steps quickly.
- If the project includes a demo video in the repo, ensure the documentation points to it and the current `.gitignore` does not exclude it.
