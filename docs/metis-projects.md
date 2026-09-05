# Metis projects in Pandora

Pandora's **Projects** dock is a local, read-only portfolio of [Metis](https://github.com/RedLynx101/Metis) active plans. Metis is the Codex execution skill that keeps long-running work grounded in a contract, phased plan, ownership, and acceptance evidence. It complements the normal launcher, music, and agent-feed docks; Pandora does not replace them or become another director.

Use the combination for one long-running agent or several independent director-led projects: Metis manages execution, while Pandora makes phases, owners, blockers, and declared capacity visible together. Managers can coordinate their own internal subagents; the shared JSON format preserves separate plan identities and authority. Large-team sizing remains subject to host/account limits, not a capability Pandora enforces. [Metis setup and connection guide →](https://github.com/RedLynx101/Metis/blob/main/docs/pandora.md)

## Register a project

1. Open **Projects** from Pandora's tray menu, or use an existing Projects dock.
2. Choose **Add dashboard…** and select the exact local Metis `.html` or `.htm` file.
3. Expand the plan to inspect its current phase, next action, ownership, waits, evidence, and source health. **Open dashboard** explicitly opens that registered file in your default browser; only open files you trust.

No paths are discovered automatically. Register each plan separately, including multiple independent plans within one project. **Remove** removes only the local registration, never the dashboard file. Registration and plan expansion preferences survive restarts in `projects.json` beside Pandora's workspace configuration. This registry is shared by Projects docks using that configuration folder.

Agents can use the same local registry through the CLI:

```powershell
.\scripts\pandoractl.ps1 project list
.\scripts\pandoractl.ps1 project add "C:\Projects\Example\dashboard.html"
.\scripts\pandoractl.ps1 project remove <registration-id>
```

`list` is read-only and returns registration JSON. `add` validates the exact dashboard before registering it. `remove` requires a known registration ID and never removes source files. With an explicit workspace path, the registry stays beside that workspace; otherwise it is `%APPDATA%\Pandora\projects.json`.

For a Metis director, check once at dashboard setup or an active-plan transition. Connect an existing source only when local project visibility is authorized. Do not install Pandora, create a dashboard solely for registration, duplicate another manager's registration, or silently repurpose a registered file for a different plan. The director remains the canonical dashboard writer.

## What the overview means

- A project groups plans by stable `projectId`; each plan retains its own `plan.id`, `task.id`, dashboard ID, and director boundary. Display names do not supply identity.
- Verified progress counts acceptance criteria marked `verified`, not activity, time spent, or manager claims of implementation. Phase status is also shown separately.
- Phase bucket widths use the number of work packages, falling back to criteria count, with a minimum width of one. Fill uses verified criteria. A completed-looking bar does not substitute for the phase acceptance gate.
- Accountable phase owner, execution lead, integration owner, and package owner remain distinct. Explicit null is **Unassigned**, never silently assigned to a director.
- Topology counts primary sessions only. Declared subagent budgets are added separately, with the number of unspecified budgets. A budget of zero differs from an unspecified budget. Declared totals are not enforced concurrency or live agent usage.
- Declared waits describe a wake condition and reported check window. Elapsed waiting time alone does not imply drift, failure, or missing liveness. Pandora does not query agents.
- Sample dashboards are prominently labeled and excluded from live totals. Duplicate dashboard IDs or project/plan pairs exclude all affected sources until resolved; Pandora does not choose a winner by file modification time.

## Read path and source health

The source contract remains **`codex-director-dashboard/v1`**, independent of Metis or Pandora branding. Pandora extracts the single `script#dashboard-state` with type `application/json`, parses strict JSON, and never executes the HTML. Unknown version-1 fields remain inert data; unsupported versions and broken identities are errors, not guessed migrations.

Reads validate required types, statuses, explicit-timezone timestamps, unique IDs, session references, current-phase references, dependency edges and cycles, phase assignment revisions, and phase acceptance consistency. Pandora also rejects a `verified` plan containing unaccepted phases. Missing required nullable fields are errors; they are not inferred as null or zero. Safe integers, nesting, and collection sizes have conservative reader limits.

**Material update** comes from `plan.updatedAt`. **Read attempt** and **successful read** describe Pandora's local read activity. None of these proves that an agent is currently running. During a source error, the last valid in-memory snapshot stays visible as **STALE**, with its old success time, and is excluded from live totals. Snapshot contents are not persisted across app restarts; only identity, revision, and material-time watermarks persist to detect regressions. A missing source after restart therefore has no invented snapshot.

Accepted identity or revision/time cannot silently move backwards. To intentionally repurpose a registered file for a different plan, remove and register it again. Prefer a different dashboard file per plan to retain clean boundaries.

Exact-file watchers debounce changes for 500 ms. A 30-second reconciliation catches missed watcher events and checks registrations across multiple docks. Reads run off the UI thread, with up to four files in parallel and a five-second per-file read cancellation deadline. Watchers and timers are disposed with the dock. Watching does not crawl subdirectories.

## Trust and authority

Only explicit, absolute local HTML files are accepted. URL, UNC/network, device, alternate-stream, symbolic-link, and junction paths are refused. The path is checked again on reads and before the explicit Open action. If a registered source later becomes disallowed, it shows a source error and remains removable; other projects continue to reconcile. No watcher is installed through a disallowed source path. Files are limited to 4 MiB, extracted state to 2 MiB, identifiers to 256 ASCII characters, JSON nesting to 40 levels, structural nodes to 40,000, primary sessions to 512, phases to 256, and registered files to 32. Very large detail collections have labeled display limits; totals still use all validated items.

All source text is rendered as WPF text, not markup or commands. Evidence paths and manager-reported text are not executed or automatically opened. Read validation checks structure and consistency, **not the truth of supplied evidence**.

Pandora owns only registration, presentation preferences, and local validation watermarks. Registry updates use a cross-process file lock, reload-before-write, a flushed temporary sibling, and atomic replacement. The serialized UTF-8 registry is limited to 256 KiB on both reads and writes; an update exceeding that limit preserves the previous file. Invalid or oversized incoming checkpoints are rejected per registration so independent valid sources can still advance. Invalid registries are reported without silent replacement. Directors own canonical plans and acceptance; managers own their permitted reports and implementation. There are no project approval buttons, checklist writes, goal creation, automatic messages, or source mutations in this integration.

Write-back or agent control would require a separate, explicit authority and conflict-resolution design. It is not implied by opening a Projects dock.

## Development

- Reader and immutable projections: `src/Pandora.Core/MetisReader.cs`, `MetisModels.cs`.
- Registration and reconciliation: `ProjectRegistryStore.cs`, `ProjectPortfolioService.cs`.
- Native view: `src/Pandora.App/ProjectsControl.xaml` and code-behind.
- Synthetic boundary tests: `tests/Pandora.Tests/MetisTests.cs` (`MetisTests.Run`) and `ProjectSafetyTests.cs` (`ProjectSafetyTests.Run`). The latter exercises oversized checkpoints, registry byte preservation, and an isolated directory junction/link without following it during cleanup.

Test data is synthetic and contains no registered personal projects.
