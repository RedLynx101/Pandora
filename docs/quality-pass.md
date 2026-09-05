# Quality pass — September 2026

Reviewed the 84 executable source, test and build/config files in baseline `974d023`, with focused review of input boundaries, persistence, lifecycle and UI behavior. Static brand assets and generated binaries were excluded from the security source inventory; actual WPF renders and brand-loading tests cover their application use. This is a bounded quality pass, not a guarantee that every bug is absent.

## Changes

- Workspace loading preserves invalid, inaccessible and future-version files. Migrations back up before replacement; snapshot fingerprints reject stale saves. CLI validation is read-only.
- Agent feeds reserve internal state IDs, enforce filename identity and UTF-8 write limits, lock entire state mutations, reject duplicate task IDs and stale revision actions, and show malformed data as errors.
- Project checkpoints have bounded identifiers and registry writes. One source becoming a junction or unavailable no longer prevents removal or healthy-project reconciliation. Source dashboards remain read-only.
- Transfers preflight bounded trees, refuse reparse traversal and retain pins on failure. Music scanning tolerates unavailable subtrees; refresh preserves selection without writing. Watchers are debounced and disposal-safe.
- Reload validates/prepares its replacement before closing working docks. Save failures remain visible; automatic AI shortcut rewriting was removed. Size repair preserves inactive monitor variants, and a second instance does not restore icons hidden by the first.
- Narrow headers prioritize the dock name and move music transport into the existing actions menu. The visualizer ships with portable builds, releases capture resources during cancellation/setup failures, truly freezes motion, and serves only its static assets on loopback.

The baseline security review confirmed three medium-severity findings: malformed-feed exceptions escaping the UI, linked-directory traversal during copy, and oversized project identifiers poisoning the registry. Each has a corresponding implementation fix and regression coverage above. Other reliability bugs were not inflated into security findings.

## Verification

Local Release build and portable App/CLI publish completed without warnings or errors. The integrated run passed 30 Core/CLI groups, 28 WPF groups with 136 offscreen control renders, six mocked-media visualizer tests, and the loopback-server integration test. PowerShell source parsing and Git whitespace checks passed. The existing user workspace also passed read-only validation.

Directory-junction refusal checks actually ran. One file-symbolic-link fixture was explicitly skipped because the local test account lacked that privilege. The server test required normal Windows execution because the restricted sandbox cannot initialize HttpListener. Test requests use fixture data; no real audio capture or desktop startup occurs in verification harnesses.

Manual review of the changed narrow-header renders confirmed readable names and unclipped controls. Offscreen tests do not establish native dragging, Explorer layering, mixed-DPI behavior, compositor transparency, real audio sharing or every supported Windows configuration. Pathname checks do not eliminate adversarial filesystem races; a failed copy can leave partial destination files. Follow [testing](testing.md) for these remaining hardware/browser acceptance checks.

An early isolated project harness had an uncaught junction-fixture setup exception, causing a Windows error dialog. The fixture was corrected; subsequent runs passed. The temporary harness now catches/report errors without Windows Error Reporting dialogs. This was a test-harness failure, not a Pandora application crash.

For release status, inspect the commit's GitHub Actions result; local build success alone is not proof of a pushed or installed release.
