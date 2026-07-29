# Security policy

## Supported versions

Until a later support policy is published, only the latest stable GitHub Release receives security fixes. The immediately previous installer is retained for controlled rollback, not ongoing support.

## Report a vulnerability

Do not open a public issue for a suspected vulnerability. Use GitHub's private vulnerability reporting feature for the repository. If that feature is unavailable, contact the repository owner through a private channel listed on their GitHub profile.

Include:

- affected version and Windows build;
- concise reproduction steps using test accounts/data;
- expected and observed impact;
- whether credentials, cross-user data, update integrity, or code execution may be involved;
- a safe proof of concept if needed.

Do not include real access/refresh tokens, passwords, cookies, Telegram bot tokens, Supabase secret keys, database dumps, prompts, completions, or private usage history. Redact usernames from paths when they are not relevant.

The project will acknowledge a complete report, assess severity, prepare a private fix, and coordinate disclosure. No guaranteed response window is promised until maintainers publish one.

## Security boundaries

The desktop is a public client and receives only a Supabase publishable key. Server secrets belong in Supabase. The experimental Codex reader is explicitly opt-in and read-only. GitHub updates require user consent and digest verification; public installers are expected to be Authenticode-signed.
