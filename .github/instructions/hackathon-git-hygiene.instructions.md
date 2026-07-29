---
applyTo: "**"
---

# Hackathon git hygiene

When working in this repository:

- Never commit secrets. Keep API keys, tokens, connection strings, and local-only config out of git history.
- Prefer environment variables, .NET user secrets, or example config files such as `.env.example` or `appsettings.Example.json`.
- Do not add ignore rules that would accidentally block the required demo asset flow. Keep `demo/` and common video formats like `.mp4` trackable unless the user explicitly chooses a different submission path.
- Write commit messages that are easy to evaluate later:
  - first line is a short summary, ideally 50 characters or less
  - optional body explains what changed and why
  - wrap body text around 72 characters when practical
- Favor direct commits on `main` only for small solo-safe work. For risky or multi-person work, prefer feature branches and pull requests.
- If adding dependencies, assets, sample code, fonts, images, models, or datasets, check that their licenses are compatible with the project and add attribution notes when required.
- Keep the repo understandable for judges: avoid noisy churn, generated junk, and unrelated file moves.
