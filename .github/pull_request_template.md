## Summary

<!-- What user or operator outcome changes? -->

## Validation

- [ ] .NET build succeeds with zero warnings
- [ ] .NET tests pass
- [ ] Deno format/check/tests pass when Edge Functions changed
- [ ] Clean Supabase migration/reset and lint pass when schema changed
- [ ] Relevant manual test cases pass

## Safety review

- [ ] No credentials, raw Codex responses, prompts, or private history are logged/committed/uploaded
- [ ] `ICodexUsageReader` isolation is preserved
- [ ] User data remains outside the install directory
- [ ] Notification and sync state remain restart/upgrade safe
- [ ] Update behavior remains explicit and user-confirmed
- [ ] Database changes use a new migration; no deployed migration was edited

## Migration and rollback

<!-- Database compatibility, desktop rollback, and any operator action. -->
