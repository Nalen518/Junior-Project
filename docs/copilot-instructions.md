# Copilot instructions for ClimaPulse

Read `AGENTS.md` in the repository root first. It is the shared rule set for all AI tools. Full rules: `docs/github-ai-agent-rules.md`.

Hard rules in short:
- Work on a feature branch (`type/short-description`). Never commit to `main`. Never force-push.
- Never merge a PR. A teammate reviews and a human merges.
- Never read, print or commit `.env`, secrets or `*.db` files.
- Ask before changing `backend/Database/schema.sql`, adding packages, moving or deleting files, or editing `.gitignore`, build files or CI.
- Keep `ClimaPulse.*` namespaces. Forms and user controls take only `ClimaPulse.Contracts` interfaces (constructor parameters), never backend classes. Only `WeatherService` calls Open-Meteo.
- Read `docs/api-contract.md` before changing a service, DTO, form or user control.
- Run `dotnet build` and `dotnet test` when they exist, and report real results. Do not invent results, links or package names.
- The team member who commits is the sole author, committer and owner. Never list Copilot or any AI tool as author, co-author or contributor in commits or PRs.

Stack: Windows desktop app. Windows Forms in `frontend/` (no Blazor, no WPF), services and repositories in `backend/`, contract in `shared/ClimaPulse.Contracts`, PostgreSQL (Npgsql), .NET 10.
