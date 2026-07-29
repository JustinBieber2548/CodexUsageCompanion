# Release process

This guide is the source of truth for desktop and database releases. Version 1 uses GitHub Releases for distribution and update discovery. Updates are never forced.

## Release principles

- Use semantic versions: `major.minor.patch`.
- Keep `main` releasable and create releases from signed, annotated tags.
- Never edit a Supabase migration after it has reached any shared environment.
- Keep at least the previous Windows installer available in GitHub Releases.
- Backward-compatible database expansion must precede a desktop release that depends on it.
- Public Windows executables and installers must be Authenticode-signed.

## 1. Select and increment the version

Choose the smallest correct increment:

- patch (`1.1.0` → `1.1.1`) for backward-compatible fixes;
- minor (`1.0.0` → `1.1.0`) for backward-compatible features;
- major (`1.1.1` → `2.0.0`) for deliberate incompatible application/API behavior.

Update these properties in `Directory.Build.props`:

```xml
<Version>1.1.0</Version>
<AssemblyVersion>1.1.0.0</AssemblyVersion>
<FileVersion>1.1.0.0</FileVersion>
<InformationalVersion>1.1.0</InformationalVersion>
```

Search for the old version and review every occurrence:

```powershell
rg --fixed-strings '1.0.0'
```

Do not replace historical examples, prior migration contents, investigation commit hashes, or release records merely because they contain the old version.

Update the release notes/changelog if one exists, then commit:

```powershell
git add Directory.Build.props
git commit -m "chore: prepare 1.1.0"
```

## 2. Add and verify Supabase migrations

For every schema change, create a new file using the current CLI:

```powershell
supabase migration new describe_the_change
```

Do not rename, reorder, squash, or modify a deployed migration. Use an additional forward migration to correct a mistake.

Test from a clean local Supabase stack:

```powershell
supabase start
supabase db reset
supabase db lint --local --level error
```

Review Row Level Security, explicit grants, ownership checks, retention, function search paths, and rollback compatibility. Run the app against the local/test project before production.

Apply production-compatible expansion migrations before releasing a desktop version that needs them:

```powershell
supabase link --project-ref YOUR_PROJECT_REF
supabase db push --dry-run
supabase db push --include-all
```

Record the applied migration version and database backup/PITR point in the release ticket. Deploy changed Edge Functions after the migration:

```powershell
supabase functions deploy ingest-usage --no-verify-jwt
supabase functions deploy telegram-webhook --no-verify-jwt
supabase functions deploy notification-check --no-verify-jwt
```

## 3. Run all tests

Install .NET 8, Deno 2.x, Supabase CLI, and Docker Desktop. Then:

```powershell
.\scripts\test.ps1 -Configuration Release
supabase start
supabase db reset
supabase db lint --local --level error
```

Expected:

- .NET restore/build succeeds with zero warnings and zero errors;
- all core, infrastructure, updater, storage, and reader tests pass;
- Deno formatting, type checks, and function tests pass;
- a clean database applies every migration and lint returns no error;
- the [manual test plan](manual-test-plan.md) has a recorded pass.

Review dependency advisories and search staged changes for secrets before tagging.

## 4. Create a signed Windows build

Configure the signing certificate and tools:

```powershell
$env:SIGNTOOL_PATH = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$env:CODE_SIGNING_CERT_THUMBPRINT = 'CERTIFICATE_THUMBPRINT'
$env:INNO_SETUP_COMPILER = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\scripts\build-windows.ps1 -Version 1.1.0
```

The script:

1. runs tests;
2. publishes a self-contained Windows x64 app;
3. signs the app when signing variables are present;
4. builds the per-user Inno Setup installer;
5. signs the installer;
6. writes `SHA256SUMS.txt`.

For a public release, verify the signature manually:

```powershell
& $env:SIGNTOOL_PATH verify /pa /all /v .\artifacts\installer\CodexUsageCompanion-1.1.0-win-x64-setup.exe
Get-FileHash .\artifacts\installer\CodexUsageCompanion-1.1.0-win-x64-setup.exe -Algorithm SHA256
```

Install over the previous release and confirm the pre-upgrade backup, preserved settings/history/credentials/deduplication/queue, clean startup, and uninstall behavior.

## 5. Tag and create the GitHub Release

Confirm the tree is clean and CI passed:

```powershell
git status --short
git log -1 --oneline
```

Create and push a signed annotated tag:

```powershell
git tag -s v1.1.0 -m "Codex Usage Companion 1.1.0"
git push origin main
git push origin v1.1.0
```

If signed Git tags are not yet available, stop and configure a verified signing identity; do not silently substitute an unsigned public release tag.

The `release.yml` workflow uses the protected `production-release` environment. It refuses to publish until `PRODUCTION_READER_VERIFIED=true`, `RELEASE_LICENSE_APPROVED=true`, a `LICENSE` file exists, and the signing certificate secrets are configured. It then validates that the tag matches `Directory.Build.props`, reruns tests, imports the protected code-signing certificate, builds/signs the installer, and creates the GitHub Release. Alternatively, an authorized maintainer may use GitHub CLI after the same checks:

```powershell
gh release create v1.1.0 `
  .\artifacts\installer\CodexUsageCompanion-1.1.0-win-x64-setup.exe `
  .\artifacts\installer\SHA256SUMS.txt `
  --title 'Codex Usage Companion 1.1.0' `
  --generate-notes `
  --verify-tag
```

Do not publish a draft until release notes clearly identify:

- the version and major user-facing changes;
- update/rollback notes;
- any database compatibility requirement;
- known limitations, including reader status;
- installer SHA-256.

After publishing, verify GitHub's release asset API exposes a `sha256:` digest because the Version 1 updater requires it. Test **Check for updates** from the previous release, review notes, decline once, then complete one update.

## 6. Roll back the desktop app

Desktop rollback does not delete or replace per-user data:

1. Exit Codex Usage Companion from the tray.
2. Download the previous signed installer from its retained GitHub Release.
3. Verify its Authenticode signature and SHA-256.
4. Run the installer as the same Windows user.
5. Start the app and confirm settings, Credential Manager session, notification state, pending synchronization, and history.

If the newer app changed local JSON in an incompatible way, first copy the whole `%LOCALAPPDATA%\CodexUsageCompanion` directory to a separate safe location. Restore the newest compatible `settings.json` and `state.json` from the timestamped `backups` directory only after preserving the current files. Credentials remain in Windows Credential Manager and are not part of file backups.

Never remove the previous GitHub Release while it remains the supported rollback target.

## 7. Handle an incompatible database migration

Avoid incompatibility through expand/migrate/contract:

1. **Expand:** add nullable columns, new tables/functions, or parallel behavior. Keep old columns and RPC signatures working.
2. **Deploy:** apply the migration and Edge Functions; verify old desktop releases still work.
3. **Migrate:** backfill in bounded batches and validate counts/constraints.
4. **Release:** ship the new desktop and monitor adoption/errors.
5. **Contract later:** only after the supported old desktop population is gone, add a new migration that removes obsolete structures.

If an incompatible migration is discovered after deployment:

- pause desktop rollout and Telegram/cron work that could compound damage;
- preserve a database backup/PITR point and collect sanitized evidence;
- roll the desktop back if the previous version still works with the expanded schema;
- deploy a new forward-fix migration—never edit the deployed file;
- restore an old database snapshot only as an incident-level last resort, because it discards writes after the snapshot;
- if restoration is required, stop all writers, communicate downtime/data-loss boundaries, restore, verify Auth/RLS/functions, and reconcile queued desktop snapshots;
- publish a corrected patch release with explicit compatibility notes.

Do not automatically downgrade the database from the desktop installer. Database and desktop rollback are separate, operator-controlled procedures.

## 8. Post-release checks

- Install from the public asset on a clean Windows 11 account.
- Upgrade from the previous version and confirm data preservation.
- Confirm automatic update checks do not repeat within 24 hours.
- Confirm Supabase ingest, retention schedule, notification schedule, and Telegram commands.
- Check sanitized logs and monitoring for elevated authentication, rejection, or delivery errors.
- Keep the previous installer, current migration record, checksums, signing verification, and manual test evidence with the release.
