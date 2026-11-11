# Security

Verificato is a local, single-user developer tool, not a sandbox for hostile applications or untrusted .NET plugins.

- Never expose the host on a network interface or forward its port to the Internet.
- Never share the one-time bootstrap URL, session cookies, or CSRF token.
- Source analysis must remain non-executing: do not replace the safe loader with MSBuildWorkspace, build/restore, or dynamic analyzer loading without a separate security design.
- Browser profile isolation does not protect the OS against an exploited Chromium. Keep browser dependencies updated.
- Locally stored console/diagnostic text may contain sensitive data despite best-effort redaction. Review before sharing a report or screenshot.
- Architecture snapshots retain source metadata (names, paths, signatures and documentation excerpts), not full source files. Source previews read original code locally on demand and can contain secrets; sharing a screenshot or exported DSL is an explicit disclosure decision.
- Source preview endpoints must stay snapshot-file-ID allowlisted, hash-checked and restricted to the approved non-symlink source root. Never add an arbitrary-path file-reading endpoint.
- Schema migration backups contain prior local reports and are not deleted automatically when reports are deleted from the main database.
- The target application may connect to external services even though Verificato does not upload reports.
- Processes running as the same OS user and port-level cookie isolation on loopback are outside the security guarantees.

For a vulnerability, use a private advisory on the repository hosting platform if enabled, or contact the maintainers privately. Do not post secrets, source code, bootstrap URLs, or exploitable details in public issues. A dedicated security contact and support policy should be established before publishing stable releases.

Please include the affected commit/version, platform, minimal reproduction with synthetic data, and impact. This is an early development version; do not assume a completed independent security audit.
