---
name: check-dalamud-updates
description: Check current official Dalamud releases and branch activity, compare an existing baseline with a new endpoint, map framework changes onto the user's configured Dalamud plugin repositories, produce patch-day impact reports, and update plugins when explicitly requested. Use when the user asks whether Dalamud changed, requests a release or commit delta, needs plugin impact triage, or wants their plugins migrated to a newer Dalamud API.
---

# Check Dalamud Updates

Use this skill for evidence-backed Dalamud patch-day review, plugin impact
analysis, and explicitly requested migration work.

## Configure This Shared Template

This public copy intentionally contains placeholders. The Codex model installing
or customizing it must replace them with the receiving user's own paths and
plugin repositories before relying on the skill.

Populate this block in the installed copy:

- Workspace root: `{{WORKSPACE_ROOT}}`
- Documentation root: `{{DOCS_ROOT}}`
- Patch-day review root: `{{PATCH_DAY_ROOT}}`
- Findings root: `{{FINDINGS_ROOT}}`
- Plugin roots:
  - `{{PLUGIN_1_NAME}}`: `{{PLUGIN_1_PATH}}`
  - `{{PLUGIN_2_NAME}}`: `{{PLUGIN_2_PATH}}`
  - `{{PLUGIN_3_NAME}}`: `{{PLUGIN_3_PATH}}`
  - Add or remove entries until every plugin the user wants maintained is
    represented.

During setup:

1. Inspect the workspace and identify the live source repository for each
   user-owned plugin.
2. Replace every placeholder token in the installed copy.
3. Add one plugin-name/path entry for every plugin in scope.
4. Verify each configured path exists and contains the expected project source.
5. Ask the user for exact paths only when they cannot be determined safely.

Do not assume all plugins share one parent directory. Treat the configured
plugin list as the source of truth. If required placeholders remain unresolved,
stop before scanning or editing and request the missing configuration. Never
silently omit a configured plugin because its path is missing or invalid.

## Path Rule

Prefer paths that exist in the current workspace. Treat paths copied from older
notes as historical until verified. Do not replace current paths with remembered
or guessed locations.

## Start Here

Open these local sources when they exist:

1. `{{DOCS_ROOT}}\README.md`
2. The user's plugin-development rules
3. The patch-day checklist under `{{PATCH_DAY_ROOT}}`
4. The previous-baseline registry
5. The local artifact or package-version registry
6. The delta-review standard and report template
7. Existing findings and impact indexes under `{{FINDINGS_ROOT}}`
8. Each configured plugin's README, manifest, project file, package lock files,
   changelog, and relevant source

Missing local documentation is not a reason to invent state. Record what is
absent and continue with verifiable official and repository evidence.

## Core Rule

Do not stop at the latest release, date, or commit. Those are endpoint metadata,
not an update review.

For a real update check, establish the old baseline and collect the complete
old-to-new delta. Separate runtime-tag requirements from later branch activity.

## Workflow

1. Read the previous Dalamud baseline.
2. Inspect local docs and artifacts for additional baseline evidence.
3. Record the old baseline:
   - previous review date;
   - previous release or tag;
   - previous commit;
   - target API level;
   - any missing or conflicting fields.
4. Check current official Dalamud state:
   - latest public release and date;
   - latest relevant branch commit and date;
   - current API documentation and supported versions.
5. Collect the delta:
   - release notes;
   - exact compare range when one can be proven;
   - commit and merged-PR list;
   - changed files of interest;
   - dependency changes in project files, central package props,
     `NuGet.Config`, and lock files.
6. Map framework changes onto each configured plugin.
7. Create or update the internal finding, official delta, public-safe report,
   impact overview, impact matrix, and one impact file per configured plugin.
8. When broken-functionality triage is requested, create one
   `<Plugin Name>.impact.md` checklist per configured plugin.
9. Update the rolling registry, dated review, and durable summary.
10. If the user explicitly requests migration or fixes, follow the
    implementation workflow below.

## Official Evidence

Use current primary sources during a real update check:

- `https://github.com/goatcorp/Dalamud`
- `https://github.com/goatcorp/Dalamud/releases`
- `https://github.com/goatcorp/Dalamud/pulls`
- `https://github.com/goatcorp/Dalamud/pulls?q=is%3Apr+is%3Amerged+base%3Amaster`
- `https://github.com/goatcorp/Dalamud/compare/<old>...<new>`
- `https://dalamud.dev/versions/`
- `https://dalamud.dev/api/`
- `https://github.com/aers/FFXIVClientStructs`
- `https://api.nuget.org/v3-flatcontainer/dalamud.net.sdk/index.json`
- `https://api.nuget.org/v3-flatcontainer/dalamudpackager/index.json`

Verify current upstream state live. Prefer release pages, compare views, merged
pull requests, source repositories, official documentation, and package feeds
over third-party summaries.

## Recent Pull Request Activity

When the user provides a pull-request review/comment URL or asks about recent
Dalamud activity:

- Resolve the link to the pull-request page.
- Record the PR number, title, branch, state, merge status, merge commit, and
  changed files.
- Check merged PRs since the old baseline date or tag.
- Check relevant open PRs involving ClientStructs, schemas, lifecycle, texture,
  client state, unlock state, plugin management, windows, or dependencies.
- Classify each item as:
  - current runtime-tag requirement;
  - post-tag merged branch watch item;
  - open, draft, or pending watch item;
  - closed-unmerged no-action item.
- Do not promote open or draft work into runtime requirements.
- Do not preserve comment or review anchors in durable reports; link to the
  stable PR, compare range, release, or repository instead.

## Plugin Impact Scope

Scan every repository listed in the configured plugin-roots block. Within each
repository, locate the active project rather than assuming the repository root
is the compilable project.

Exclude generated or historical material such as:

- `bin`
- `obj`
- `backups`
- `archive`
- `Previous Releases`
- packaged releases
- vendored plugin references

Do not scan only shared helper filenames. Inspect the complete live C# source
surface and configuration for each plugin.

At minimum, check:

- SDK, packager, manifest API level, target framework, and plugin version;
- `IAddonLifecycle`, `IAgentLifecycle`, listener registration, listener
  removal, and original-call prevention;
- `IClientState`, local-player assumptions, and client-idle behavior;
- direct input-timer usage and anti-AFK logic;
- direct `FFXIVClientStructs`, `AtkValue`, `AtkUnitBase`, `Agent`, `Addon`,
  `UIModule`, `ObjectKind`, and `ContentId` usage;
- removed or renamed native enum members, compatibility aliases, and helper
  methods;
- hooks, address-based hooks, signature-based hooks, signature scanning,
  member-function pointers, patches, and local signature registries;
- texture providers, texture readback, raw-image APIs, texture wrappers,
  draw-list textures, and game-file texture loading;
- unlock-state services and class/job, notebook, quest, or unlock-link checks;
- chat services, handled chat events, chat objects, and command helpers;
- Lumina sheets, row references, generated sheets, and `GetExcelSheet`;
- window systems, UI builders, windows, scaling, and immediate-mode UI calls;
- IPC wrappers and external plugin dependencies discovered in source;
- configuration migration, serialization, and saved-data compatibility.

Treat unchecked checklist items as broken or unsafe until patched and validated.
Do not claim live in-game reproduction unless it actually occurred.

## Impact File Standard

Create one Markdown impact file for every configured plugin. Every file must
include:

- plugin name and verified source root;
- baseline and endpoint;
- project/package/manifest metadata;
- relevant scan counts and direct code surfaces;
- current runtime-tag requirements;
- post-tag watch items;
- impact assessment;
- a section titled exactly `Before > After Codebase Examples`;
- current result;
- next validation step.

For each impacted surface in `Before > After Codebase Examples`, include:

- target source filenames;
- a `Before` C# snippet showing the old, fragile, or search-for pattern;
- an `After` C# snippet showing the safer current pattern;
- the upstream evidence that makes the change relevant;
- a note when the plugin already uses the `After` pattern, making it a
  validation target rather than a required edit.

Keep examples specific enough for a later code pass to locate and patch the
plugin. Do not leave impact files as risk summaries only.

## Implementation Boundary

An update check or impact review does not authorize source changes, builds,
version bumps, packaging, or releases.

When the user explicitly asks to update or migrate their plugins:

1. Preserve unrelated working-tree changes.
2. Create recoverable backups according to the repository's own rules.
3. Apply only changes supported by the proven delta and plugin scan.
4. Keep plugin behavior and safety semantics stable unless the user requests a
   behavior change.
5. Update user-facing documentation and changelogs when the repository requires
   them.
6. Restore dependencies and build with the SDK selected by the repository.
7. Run available static checks and focused tests.
8. Report build/static validation separately from required in-game testing.
9. Do not bump plugin versions, package releases, publish artifacts, commit,
   push, or perform live game actions unless the user explicitly requests those
   actions.

## Output Standard

For a serious review, include:

- old baseline;
- new endpoint;
- local artifact or documentation state;
- release-note summary;
- commit and merged-PR delta;
- dependency and package-lock delta;
- changed files of interest;
- current runtime-tag requirements;
- post-tag branch watch items;
- plugin impact folder path;
- one impact summary per configured plugin;
- actionable before/after examples;
- risk assessment;
- validation status;
- follow-up work.

Suggested generic targets:

- Rolling registry:
  `{{PATCH_DAY_ROOT}}\.Last checked.md`
- Dated review:
  `{{PATCH_DAY_ROOT}}\YYYY_MM_DD - Dalamud.md`
- Durable summary:
  `{{PATCH_DAY_ROOT}}\Dalamud\YYYY_MM_DD - Summary.md`
- Internal finding:
  `{{FINDINGS_ROOT}}\NN - YYYY-MM-DD Dalamud <old> to <new> Findings.md`
- Range overview:
  `{{FINDINGS_ROOT}}\Impacts\<old version> to <new version>\README.md`
- Impact matrix:
  `{{FINDINGS_ROOT}}\Impacts\<old version> to <new version>\Impact Matrix.md`
- Per-plugin impact:
  `{{FINDINGS_ROOT}}\Impacts\<old version> to <new version>\<Plugin Name>.md`
- Broken-functionality checklist:
  `{{FINDINGS_ROOT}}\Impacts\<old version> to <new version>\<Plugin Name>.impact.md`
- Public report:
  `{{FINDINGS_ROOT}}\Public\Dalamud <old> to <new> - Public Report.md`

Adapt filenames to the receiving repository's conventions without losing the
baseline, endpoint, date, or per-plugin separation.

## Public-Safety Rule

When producing a public report:

- omit private plugin names unless the user explicitly approves them;
- omit machine-local paths, usernames, private source snippets, credentials,
  tokens, internal URLs, and proprietary implementation details;
- summarize upstream changes and general migration patterns;
- use neutral placeholders where a private identifier would otherwise appear;
- scan the final report for drive-letter paths, home-directory paths,
  credentials, and private implementation markers before calling it
  public-safe.

## Fast Rule

If the old baseline is incomplete, say so and lower confidence.

Do not fabricate a release, tag, commit, compare range, test result, or plugin
path.
