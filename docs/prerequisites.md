# Development prerequisites

This report records the Milestone 0 environment audit for Codex Usage
Companion. No application code or system-wide configuration was changed during
the audit.

Audit date: 2026-07-29

Development computer: Windows 11 Home Single Language, 64-bit, build 26200

Workspace: `C:\Users\user\Desktop\Codex Companion`

## Current status

| Tool | Detected version | Status | When it is needed |
| --- | --- | --- | --- |
| Windows | 11, build 26200, x64 | Ready | All desktop milestones |
| .NET SDK | 8.0.423 | Ready | Desktop build and tests |
| .NET Windows Desktop runtime | 8.0.29 | Ready | WPF development |
| Git | 2.55.0.windows.3 | Ready | All milestones |
| Windows Package Manager | 1.29.280 | Ready | Optional tool installation |
| Visual Studio Code | Installed | Optional editor available | Any milestone |
| GitHub CLI | Not detected | Deferred | Repository and release operations |
| Node.js and npm | Not detected | Deferred | Supabase Edge Functions |
| Supabase CLI | Not detected | Deferred | Supabase migrations and functions |
| Docker Desktop | Not detected | Deferred | Local Supabase and RLS testing |
| Visual Studio / Build Tools | Not detected | Optional | Graphical WPF development |
| Windows signing tools | Not detected | Deferred | Signed public release |
| PowerShell 7 (`pwsh`) | Not detected | Optional | Windows PowerShell is sufficient |

The available .NET 8 SDK is the latest stable supported SDK installed in this
environment, so Version 1 will initially target .NET 8. A framework upgrade must
be an explicit, tested project change rather than an automatic prerequisite
change.

## Repository baseline

The workspace was empty at the start of this audit. It is now a local Git
repository with the default branch named `main`.

The repository ignore rules exclude:

- .NET build and test output;
- IDE-specific user files;
- local settings, databases, logs, backups, and diagnostics;
- environment files, credentials, private keys, and signing certificates;
- Supabase local state;
- Node dependencies; and
- installers and generated release artifacts.

Git does not currently have an author name or email configured. Version control
still records file changes, but Git will require an identity before the first
commit. An identity will not be invented or applied globally without the
developer's choice.

## Runtime data boundary

Installed application files and mutable user data will be separated:

- Installation files: selected later by the packaging strategy.
- Mutable data: `%LOCALAPPDATA%\CodexUsageCompanion`.
- OS-protected secrets: Windows Credential Manager.
- Settings backups: a versioned `backups` directory beneath the per-user data
  directory.

The mutable data directory will contain settings, the latest normalized usage
snapshot, notification deduplication state, the bounded synchronization queue,
local history, sanitized logs, and update-check state. An installer or updater
must never replace this directory.

## Update and migration baseline

Version 1 will use semantic versions in `major.minor.patch` form. The installed
version will come from the application assembly/package metadata.

GitHub Releases will be the release and update source. Update behavior must:

- expose the current version and a manual update check;
- check automatically no more than once per UTC day;
- compare releases using semantic-version rules;
- show the new version and release notes;
- obtain confirmation before download and again before installation when
  appropriate;
- tolerate GitHub, network, and malformed-release failures without stopping the
  application;
- back up settings and validate the staged configuration before upgrade; and
- preserve the previous installer or provide tested rollback instructions.

Supabase migrations will use ordered, versioned SQL files. A migration is
immutable after deployment. Every later schema change requires a new migration.
Potentially incompatible changes must use an expand-and-contract strategy where
possible and must have a documented database backup, forward-fix, and recovery
procedure.

## Tools deferred until needed

No missing tool blocks Milestones 0 through 2. Installations will be introduced
one group at a time in the milestone that first requires them:

1. GitHub CLI before GitHub repository or release automation.
2. Node.js current LTS and the project-local Supabase CLI before Edge Function
   development.
3. Docker Desktop before local Supabase migration, RLS, and integration tests.
4. Windows signing tools before a signed public release candidate.

Deferring these tools keeps the initial setup small and avoids changing the
computer before the related design is approved.

## Milestone 0 verification

From `C:\Users\user\Desktop\Codex Companion`, run:

```powershell
git status --short --branch
dotnet --list-sdks
git --version
```

Successful output should identify branch `main`, list SDK `8.0.423`, and report
Git 2.55.0. The first command will show the new baseline files until they are
committed.

If `dotnet` or `git` is not recognized in a new terminal, restart Windows
PowerShell so it reloads the installed PATH. If the command remains missing, do
not edit PATH manually; install or repair the affected tool before continuing.
