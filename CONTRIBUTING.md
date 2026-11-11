# Contributing

1. Read `docs/plan-techniczny-mvp.md` and `docs/implementation-status.md`.
2. Install .NET 10 SDK, Node.js 24 LTS and PowerShell 7.
3. Run `npm --prefix src/Verificato.UI ci`, build the UI, and install Chromium using `scripts/install-browser.ps1`.
4. Use `scripts/dev.ps1` for UI hot reload or `scripts/start.ps1` for one-host mode.
5. Before submitting a small, focused change, run:

```powershell
npm --prefix src/Verificato.UI run build
npm --prefix src/Verificato.UI test
dotnet test Verificato.slnx
npm --prefix src/Verificato.UI run test:e2e
```

Core must not reference Playwright, Roslyn, ASP.NET Core or SQLite. Keep adapter-specific types at module boundaries. Add tests for changes to run transitions, redaction, origin scope, persistence and read-only source loading.

Use synthetic fixtures. Never commit SQLite databases, real source snapshots, logs with credentials, browser profiles, generated dist/node_modules, or bootstrap URLs. Respect cancellation, bounded memory and the distinction between execution status, completeness and quality outcome.

No build/restore or repository-supplied code execution may be added to the default source analysis path. New dependencies must be pinned, audited and documented in third-party notices.

The initial UI language is Polish. Keep status text accessible and do not encode severity only through color. Scope claims and README instructions must match actual tested behavior.
