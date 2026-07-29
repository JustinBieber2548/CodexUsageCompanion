# Contributing

## Before changing code

Read [Architecture](docs/architecture.md), [Security](docs/security.md), and the source-specific findings in [data-source-investigation.md](docs/data-source-investigation.md).

Preserve these invariants:

- `ICodexUsageReader` remains the boundary around Codex/CodexBar compatibility logic.
- Mock mode remains the default until a real source is demonstrably safe and reliable.
- User data stays outside the installation directory.
- Credentials never enter logs, diagnostics, Git, or Supabase normalized payloads.
- Reset time alone never produces a 100% claim.
- Notification event keys survive restart and prevent duplicate delivery.
- The desktop never contains a Supabase secret key or Telegram bot token.
- No silent or forced updater behavior.

## Development

```powershell
.\scripts\test.ps1 -Configuration Debug
dotnet run --project .\src\CodexUsageCompanion.App
```

Use the mock scenarios for UI and notification development. Add behavioral tests for fixes and boundary cases.

## Database changes

Use:

```powershell
supabase migration new concise_change_name
```

Never edit a migration that has been deployed. Prefer expand/migrate/contract compatibility and add a forward-fix migration for mistakes. Test a clean `supabase db reset` and RLS isolation.

## Pull requests

Keep changes focused, explain privacy/security impact, include test evidence, note migrations and rollback behavior, and update documentation. Do not commit build output, `.env` files, local data, tokens, or signing material.

No project license has been selected, so outside contributions should not be accepted until the owner adds a contribution/licensing policy.
