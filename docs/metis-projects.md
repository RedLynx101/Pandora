# Metis projects in Pandora

Pandora's **Projects** dock is a local, read-only portfolio of Metis active plans. It complements the normal launcher, music, and agent-feed docks; it does not replace them or become another director.

## Register a project

1. Create a Projects dock from Pandora's dock menu.
2. Choose **Add dashboard…** and select the exact local Metis `.html` or `.htm` file.
3. Expand the plan to inspect its current phase, next action, ownership, waits, evidence, and source health. **Open dashboard** explicitly opens that registered file in your default browser; only open files you trust.

No paths are discovered automatically. Register each plan separately, including multiple independent plans within one project. **Remove** removes only the local registration, never the dashboard file. Registration and plan expansion preferences survive restarts in `projects.json` beside Pandora's workspace configuration. This registry is shared by Projects docks using that configuration folder.

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

Only explicit, absolute local HTML files are accepted. URL, UNC/network, device, alternate-stream, symbolic-link, and junction paths are refused. The path is checked again on reads and before the explicit Open action. Files are limited to 4 MiB, extracted state to 2 MiB, JSON nesting to 40 levels, structural nodes to 40,000, primary sessions to 512, phases to 256, and registered files to 32. Very large detail collections have labeled display limits; totals still use all validated items.

All source text is rendered as WPF text, not markup or commands. Evidence paths and manager-reported text are not executed or automatically opened. Read validation checks structure and consistency, **not the truth of supplied evidence**.

Pandora owns only registration, presentation preferences, and local validation watermarks. Registry updates use a cross-process file lock, reload-before-write, a flushed temporary sibling, and atomic replacement. Invalid registries are reported without silent replacement. Directors own canonical plans and acceptance; managers own their permitted reports and implementation. There are no project approval buttons, checklist writes, goal creation, automatic messages, or source mutations in this integration.

Write-back or agent control would require a separate, explicit authority and conflict-resolution design. It is not implied by opening a Projects dock.

## Development

- Reader and immutable projections: `src/OrbitDock.Core/MetisReader.cs`, `MetisModels.cs`.
- Registration and reconciliation: `ProjectRegistryStore.cs`, `ProjectPortfolioService.cs`.
- Native view: `src/OrbitDock.App/ProjectsControl.xaml` and code-behind.
- Synthetic boundary tests: `tests/OrbitDock.Tests/MetisTests.cs` (`MetisTests.Run`).

Legacy internal source directories and namespaces remain `OrbitDock` for compatibility; product UI and release branding are Pandora. Test data is synthetic and contains no registered personal projects.
