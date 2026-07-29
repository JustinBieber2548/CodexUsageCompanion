# Codex usage data-source investigation

## Executive conclusion

Version 1 should use an isolated, opt-in OAuth usage reader compatible with the
current CodexBar approach. It may read the existing local Codex OAuth file and
call the quota-only usage endpoint, but it must never upload, persist, log, or
display the credential. The reader will not refresh or rewrite Codex's
credential file in Version 1.

Mock mode remains the default for development. The OAuth reader is marked
experimental until a user explicitly enables it and compares its values with
the Codex interface on this Windows computer.

A local Codex CLI RPC reader is the preferred fallback when a separately
executable Codex CLI is available. The Microsoft Store-packaged Codex executable
found during this audit could not be launched from PowerShell, so this fallback
is not currently usable on the development computer.

No prompt, model request, or Codex job is required by either source.

## Investigation scope

Repository: [steipete/CodexBar](https://github.com/steipete/CodexBar)

Inspected revision:
`bd88087a6c4e79f73e7c5409042b2b908a9dbc45`

Revision date: 2026-07-29

The investigation used a temporary, shallow checkout outside this project. No
CodexBar source was copied into the application.

## 1. Relevant CodexBar files and modules

| File or module | Relevance |
| --- | --- |
| `docs/codex.md` | Current source order, endpoint behavior, and privacy notes |
| `Sources/CodexBarCore/Providers/Codex/CodexProviderDescriptor.swift` | Selects OAuth, CLI, and optional web strategies |
| `Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthCredentials.swift` | Resolves and parses `auth.json` |
| `Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift` | Decodes quota windows and calls the usage endpoint |
| `Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexTokenRefresher.swift` | Refreshes OAuth tokens in CodexBar |
| `Sources/CodexBarCore/UsageFetcher.swift` | Starts `codex app-server` and performs JSON-RPC rate-limit reads |
| `Sources/CodexBarCore/Providers/Codex/CodexStatusProbe.swift` | Manual diagnostic `/status` TUI parser |
| `Sources/CodexBarCore/Providers/Codex/CodexWebDashboardStrategy.swift` | Optional macOS WebKit and browser-cookie path |
| `Sources/CodexBarCore/OpenAIWeb/*` | Web dashboard loading, scraping, and cookie import |
| `Package.swift` | Swift platforms, dependencies, macOS gates, and Linux CLI targets |
| `LICENSE` | MIT license and required notice |

The CodexBar repository contains far more provider functionality than this
project needs. Reusing the entire project would add unrelated providers,
Swift-specific infrastructure, browser access, and macOS UI dependencies.

## 2. Authentication and source methods

### OAuth usage API

CodexBar reads:

- `%CODEX_HOME%\auth.json` when a Codex home override exists; otherwise
- the current user's `.codex\auth.json`.

For OAuth credentials it sends an authenticated `GET` request to:

`https://chatgpt.com/backend-api/wham/usage`

The response includes a primary window, a secondary window, reset times,
window durations, and optional credit information. CodexBar maps these to its
session and weekly lanes.

CodexBar may refresh and rewrite the OAuth tokens. Codex Usage Companion will
not do that in Version 1. Avoiding token refresh prevents this application from
becoming another writer to a credential file owned by Codex. An expired or
rejected token becomes an `authentication_required` result, and the user is
directed to sign in through Codex.

The usage endpoint is not documented as a public, stable OpenAI API. Its use in
this project is therefore experimental even though a mature open-source client
already uses it.

### Codex CLI RPC

CodexBar starts:

```text
codex -s read-only -a untrusted app-server
```

It communicates over JSON-RPC on standard input and output using:

- `initialize`;
- `initialized`;
- `account/read`; and
- `account/rateLimits/read`.

This source avoids direct credential parsing and returns the same categories of
quota information. It must launch a local executable and use strict time,
payload-size, and process-lifetime bounds.

### Codex CLI TUI diagnostic parser

CodexBar can start the interactive Codex terminal UI, send `/status`, and parse
rendered `5h limit` and `Weekly limit` lines. Its current documentation classifies
this as a manual diagnostic path because the interactive UI can trigger update
or authentication flows.

This project will not use that path for automatic monitoring.

### OpenAI web dashboard

CodexBar can optionally load `https://chatgpt.com/codex/settings/usage` in a
hidden macOS WebKit view and import browser cookies. It can also accept a manual
cookie header.

This path is macOS-specific, has a larger privacy surface, can increase network
and battery use, and is unnecessary for the two required usage windows. It is
excluded from Version 1.

### Local session and cost logs

CodexBar can scan local JSONL session files to estimate token cost. Those files
do not provide an authoritative remaining allowance and may contain unrelated
session metadata. This project will not scan or upload them for quota
monitoring.

## 3. Platform-specific dependencies

| Area | Portability |
| --- | --- |
| OAuth JSON parsing and HTTP response mapping | Platform-neutral and suitable for a small C# reimplementation |
| OAuth token file permissions and atomic writes | CodexBar uses POSIX APIs; not portable and not needed by the read-only Version 1 reader |
| CLI JSON-RPC protocol | Platform-neutral |
| CLI executable discovery and process handling | Requires a Windows implementation and packaged-app testing |
| CLI PTY session | CodexBar uses Darwin/Linux terminal and process APIs; not directly portable |
| Web dashboard | Uses AppKit, WebKit, macOS cookie stores, and Keychain; not portable |
| Menu application and updater | macOS-specific |
| Core Swift package | Built for macOS and Linux; no supported Windows product is published |

Swift source reuse is not practical for the Windows desktop application. The
small protocol and response models can be independently implemented in C#
behind `ICodexUsageReader`.

## 4. Windows portability assessment

The OAuth data path is the most portable because it needs only:

- a narrowly resolved local file;
- `System.Text.Json`;
- an HTTPS client with redirects and cookies disabled;
- an authorization header held only in memory; and
- bounded response parsing.

The CLI RPC path is technically portable, but executable discovery is a current
blocker. This audit found:

```text
C:\Program Files\WindowsApps\OpenAI.Codex_...\app\resources\codex.exe
```

PowerShell could resolve that executable but launching `codex --version` or
`codex app-server --help` returned `Access is denied`. A separately installed
Codex CLI may resolve this later.

The web and PTY implementations are not reasonable Version 1 ports.

## 5. Security implications

### OAuth reader

Benefits:

- reads a known file rather than browser databases;
- requires no password;
- sends the token only to a fixed HTTPS `chatgpt.com` host;
- does not start a model job; and
- can parse exact reset timestamps.

Risks and controls:

- **Credential theft:** never copy the token to settings, history, logs,
  diagnostics, Supabase, crash reports, or exceptions.
- **Path substitution:** accept only the ambient Codex home or a user-selected
  absolute Codex home; reject network paths and unexpected redirections.
- **Endpoint override:** no user-configurable endpoint in production builds.
- **Redirect leakage:** disable automatic redirects for the authenticated
  request.
- **Oversized response:** enforce a small response-size limit.
- **Credential races:** open read-only with sharing compatible with Codex and do
  not rewrite the file.
- **Error leakage:** map HTTP and parser failures to sanitized error codes.

### CLI RPC

Benefits:

- Codex owns credential access;
- the companion receives only normalized account and quota data; and
- the server runs with read-only and untrusted flags.

Risks and controls:

- bound startup and method timeouts;
- cap stdout and stderr buffers;
- never include command lines or raw output in cloud data;
- terminate only the child process started by this application; and
- reject unknown or malformed response shapes.

### Web and browser sources

Browser cookies can authorize broad account actions and browser databases may
contain unrelated browsing data. These sources are excluded.

## 6. Reliability assessment

| Source | Reliability | Notes |
| --- | --- | --- |
| OAuth usage API | Medium | Fast and structured, but the endpoint is not a public stable API and credentials expire |
| Codex CLI RPC | Medium to high when executable | Structured and credential-isolated, but process startup and packaged-app permissions vary |
| CLI `/status` TUI | Low for background use | Rendered text and interactive flows can change |
| Web dashboard scrape | Low to medium | DOM, login, cookies, and anti-bot flows can change |
| Local session logs | Unsuitable for quota | Useful for estimates, not authoritative remaining allowance |
| Mock reader | High for development | Deterministic but not production data |

All production reads must fail closed: keep and mark the last known snapshot
stale instead of inventing a value.

## 7. Quota consumption

| Source | Intentionally consumes Codex quota? |
| --- | --- |
| OAuth usage endpoint | No; it reads account quota metadata |
| CLI `account/rateLimits/read` | No; it reads account metadata |
| CLI `/status` | No prompt is intended, but the interactive path is excluded from automatic monitoring |
| Web dashboard | No model request, but higher local/network overhead |
| Local session scan | No network or model request |
| Mock reader | No |

The application must never send a prompt, create a thread, submit code, or call
an inference endpoint.

## 8. Recommended Version 1 source

Implement `CodexOAuthUsageReader` in its own assembly with these defaults:

1. Disabled until the user explicitly opts in.
2. Read the existing Codex OAuth file in memory.
3. Send one bounded HTTPS quota request.
4. Never refresh or save OAuth tokens.
5. Normalize only the two reported windows.
6. Preserve source precision and window names/durations.
7. Return sanitized authentication, network, unsupported-source, and parser
   errors.
8. Apply the normal two- or five-minute polling policy and failure backoff.

This satisfies the preferred-source rule while keeping the unstable code behind
`ICodexUsageReader`.

## 9. Fallback source

Implement an independently replaceable `CodexCliRpcUsageReader` after a
Windows-executable Codex CLI can be launched and the RPC response has been
captured safely.

Until then:

- use `MockCodexUsageReader` for development and automated tests;
- report live reading as experimental or unavailable;
- do not fall back to browser cookies, UI Automation, OCR, or screenshots.

## 10. Unresolved machine tests

- Enable the OAuth reader through the application's explicit consent screen.
- Compare both percentages and reset times with the current Codex interface.
- Confirm a quota read does not create a Codex thread, job, or usage event.
- Observe behavior when Codex refreshes or replaces `auth.json` during a read.
- Verify a 401/403 produces `authentication_required` without token text in
  logs.
- Install or expose a separately executable Codex CLI and verify
  `account/rateLimits/read` on Windows.
- Confirm the packaged Microsoft Store Codex executable remains inaccessible
  from a normal per-user process before classifying it as unsupported.

Until those tests are completed, the production reader remains experimental.

## Licensing and attribution

CodexBar is licensed under the MIT License:

```text
Copyright (c) 2026 Peter Steinberger
```

An independent C# implementation of the observed data contract is preferred to
copying Swift source. If any substantial CodexBar source is copied later, its
copyright and MIT notice must be included in the distributed third-party
notices.
