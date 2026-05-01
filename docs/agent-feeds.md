# Agent Feeds

Agent feed docks let local agents publish compact OrbitDock panels without giving OrbitDock access to Gmail, Google Calendar, or other external systems. Agents do the work elsewhere, then write a local feed through `orbitdockctl`.

## Storage

Default location:

```text
%APPDATA%\OrbitDock\AgentFeeds
```

Each feed is one JSON file:

```text
%APPDATA%\OrbitDock\AgentFeeds\morning-brief.json
```

OrbitDock keeps read state and local checklist completion in:

```text
%APPDATA%\OrbitDock\AgentFeeds\state.json
```

Agents should prefer the CLI. The store writes atomically with a lock file so concurrent local agents do not corrupt feed state.

Feed files are local-only but still treated as untrusted agent input. OrbitDock rejects oversized feed files, very long text fields, and feeds with excessive sections or items so a broken automation cannot freeze the desktop surface with an accidentally huge payload.

## CLI

```powershell
.\scripts\orbitdockctl.ps1 agent-feed list
.\scripts\orbitdockctl.ps1 agent-feed show morning-brief
.\scripts\orbitdockctl.ps1 agent-feed publish morning-brief --title "Morning Brief" --summary "Two items need attention." --status attention
.\scripts\orbitdockctl.ps1 agent-feed write morning-brief --file .\morning-brief.feed.json
.\scripts\orbitdockctl.ps1 agent-feed mark-read morning-brief
.\scripts\orbitdockctl.ps1 agent-feed mark-unread morning-brief
.\scripts\orbitdockctl.ps1 agent-feed complete morning-brief email-1
.\scripts\orbitdockctl.ps1 agent-feed reopen morning-brief email-1
.\scripts\orbitdockctl.ps1 agent-feed validate .\morning-brief.feed.json
```

`publish` is convenient for simple agent output. `write` is better when an agent can produce the full schema.

## Feed Document

```json
{
  "schemaVersion": 1,
  "feedId": "morning-brief",
  "title": "Morning Brief",
  "sourceAgent": "Codex morning brief",
  "icon": "\uE9D9",
  "status": "attention",
  "revision": "2026-04-26T073000-04",
  "updatedUtc": "2026-04-26T11:30:00Z",
  "expiresUtc": "2026-04-27T11:30:00Z",
  "summary": "Classes are stacked today and two messages need review.",
  "sections": [
    {
      "id": "attention",
      "title": "What Needs Attention",
      "kind": "checklist",
      "items": [
        {
          "id": "email-financial-aid",
          "text": "Review the financial aid email",
          "detail": "It stayed in Needs Review because it may require a same-day response.",
          "priority": "p1",
          "state": "open",
          "source": "Gmail"
        }
      ]
    },
    {
      "id": "agenda",
      "title": "Agenda",
      "kind": "agenda",
      "items": [
        {
          "id": "class-genai",
          "text": "9:30-10:50 - Generative AI Lab",
          "priority": "p2",
          "source": "Google Calendar"
        }
      ]
    }
  ]
}
```

Valid `status` values are `quiet`, `attention`, `actionNeeded`, and `error`.

Valid section `kind` values are `summary`, `checklist`, `agenda`, `items`, and `markdown`.

Checklist item `state` can be `open`, `done`, or `dismissed`. OrbitDock checkbox changes are local only; they do not change Gmail, Google Calendar, or any external system.

## Morning Brief Pattern

The existing morning brief automation should still produce the normal Markdown brief in the Codex thread. After that, it can publish a feed:

```powershell
$checklist = @(
  "Review high-priority Needs Review item from Gmail",
  "Check the 2:00 PM calendar commitment"
) | ConvertTo-Json
$checklistPath = Join-Path $env:TEMP "orbitdock-morning-brief-checklist.json"
$checklist | Set-Content -LiteralPath $checklistPath

.\scripts\orbitdockctl.ps1 agent-feed publish morning-brief `
  --title "Morning Brief" `
  --summary "Two items need attention before the afternoon." `
  --checklist-file $checklistPath `
  --status attention
```

For richer output, have the automation write the full JSON document and call:

```powershell
.\scripts\orbitdockctl.ps1 agent-feed write morning-brief --file .\morning-brief.feed.json
```

OrbitDock will show an unread badge when the revision changes, plus a count of open attention items.
