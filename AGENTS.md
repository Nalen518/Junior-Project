# AGENTS.md

Instructions for AI coding agents working in this repository.

## Project

ClimaPulse is a Windows desktop app (WPF) that compares live weather against 30-year historical baselines (z-score) to detect climate anomalies, show a multi-city watchlist, give advisories and keep a historical anomaly ledger. It is a university group project (UGM DTETI, group "Mendoan").

- Frontend: WPF (`Window` and `UserControl` screens, XAML) in `frontend/` (Windows only). Blazor and Razor are not allowed for the UI. WPF replaced Windows Forms on 2026-11-25 (SRD 1.3).
- Backend: services, repositories and `WeatherService` in `backend/` (a class library, no web server)
- Contract between them: interfaces, DTOs, enums and exceptions in `shared/ClimaPulse.Contracts` (once created). Read `docs/api-contract.md` before touching a service, DTO or component.
- Database: PostgreSQL through Npgsql, schema in `backend/Database/schema.sql`. Connection string from `CLIMAPULSE_DB_CONNECTION` (see `backend/.env.example`).
- Requirements: `docs/SRD.md`.
- Class diagrams: `docs/class-diagram/`

## Architecture rules

- Windows and user controls receive only `ClimaPulse.Contracts` interfaces through their constructors. Never a repository, `WeatherService` or another backend class. Only `frontend/App.xaml.cs` (the composition root) creates backend classes.
- Services return DTOs, never `ClimaPulse.Models` entities. z-score and tier logic lives only in the backend.
- Only `WeatherService` calls Open-Meteo.
- C# namespaces stay `ClimaPulse.*` (Models, Repositories, Services, Strategies, Enums). Do not rename them.
- Timestamps are UTC ISO 8601 text (`yyyy-MM-ddTHH:mm:ssZ`) in the database.
- `SeverityTier.Normal` is never stored. Stored tiers are watch, warning, extreme.
- `AnomalyEvent` keeps snapshots of the baseline mean and standard deviation. Do not join back to `Baseline` to display old events.

## Git rules (hard)

- Work only on a feature branch named `type/short-description`. Never commit to `main`.
- Never force-push, rewrite history, delete branches, or touch another person's branch.
- Never merge a PR. A human reviews and merges.
- Commit messages use Conventional Commits, for example `feat(watchlist): implement IWatchlistService.AddAsync`.
- Keep each change to one task and under about 400 changed lines.

## Ask the human before

- Changing `backend/Database/schema.sql` (then also update `schema.dbml`, `seed.sql`, models and `DbMapping`).
- Adding, removing or upgrading any package. Check the package exists on the official registry.
- Moving or deleting files, or changing `.gitignore`, build files or CI.
- Changing an interface, DTO or exception in `shared/ClimaPulse.Contracts` that someone else already uses.

## Secrets (hard)

- Never read, print, copy or commit `.env`, tokens, keys or `*.db` files.
- Never put secrets in code, tests, docs or commit messages.

## Quality

- Run `dotnet build` and `dotnet test` once the solution exists, and report the real output.
- Do not disable tests or warnings to get a green build.
- Say plainly when something could not be run or verified. Do not invent results, links or package names.
- Handle empty results and a zero standard deviation before dividing.
- Use `CultureInfo.InvariantCulture` for numbers in export files and URLs (Indonesian Windows uses a decimal comma).
- Never block the UI thread: no `.Result` or `.Wait()` in windows or user controls. Use `async void` event handlers that `await` the service.
- Match the existing style. Keep explanations in docs plain and short.

## PRs

Do not mention AI tools as authors or contributors in commits or PR text.
