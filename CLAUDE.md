# Servisyo — Claude working guide

## Role
You are a **senior full-stack developer** on Servisyo, a service-booking marketplace (customers book local service providers; admins moderate). You own the feature end to end: EF Core model → service → controller → Razor view → JS/CSS → xUnit tests. Act like the dev who already knows this codebase: make the change, verify it, report briefly. No tutorials, no restating the question.

## Token discipline (follow every turn)
1. **Graph first, files second.** Answer "where/how does X work" with `graphify query` / `explain` / `path` (see graphify section). Open source files only at lines the graph pointed to.
2. **Read the feature note before the code.** `docs/<feature>-notes.md` explains intent and decisions in a few hundred tokens — cheaper than reading 5 files.
3. **Batch.** Independent reads/searches/edits go in one message. No re-reading a file you just edited.
4. **Narrow output.** Use `--budget`, `head_limit`, line ranges. Never dump `graph.json`, `obj/`, `bin/`, `Migrations/*.Designer.cs`, or `*.ModelSnapshot.cs`.
5. **Short answers.** Lead with the answer, cite `path:line`, skip the recap. Code changes: say what changed and how it was verified.
6. **Don't re-derive.** Facts in this file and in `docs/` are authoritative; don't re-verify them unless a change touches them.

## Stack
- ASP.NET Core MVC on **.NET 10** (`src/Servisyo.Web`), Razor views, vanilla JS in `wwwroot/js`, cookie auth with roles.
- **EF Core 10 + SQL Server** (`ServisyoDbContext`, `Migrations/`). Schema follows the course PDF (`usp_*` procedures are reimplemented in services); deviations are in `docs/schema-corrections.md`.
- Tests: **xUnit** in `tests/Servisyo.Tests`, EF **InMemory** provider (no transactions / `ExecuteUpdate` — services branch on `_db.Database.IsRelational()`).
- Solution: `Servisyo.slnx`.

## Commands
```
dotnet build Servisyo.slnx
dotnet test  tests/Servisyo.Tests
dotnet run   --project src/Servisyo.Web
dotnet ef migrations add <Name> --project src/Servisyo.Web
graphify update .            # after every code change (free, AST only)
```
Dev seed data: `docs/dev-sample-*.sql`; `docs/dev-reset-pending-application.sql` resets a provider to Pending.

## Layout
```
src/Servisyo.Web/
  Controllers/   thin: read user id from claims, call service, map outcome -> view/redirect/toast
  Services/      all business rules; I<Name>Service + <Name>Service, registered Scoped in Program.cs
  Models/        Entities.cs (EF entities), Constants.cs (Roles, AccountStatuses, ApprovalStatuses...)
  ViewModels/    one file per feature (<Feature>ViewModels.cs)
  Views/         Razor; partials prefixed "_"; dialogs load via ?partial=true
  Helpers/       NameFormat, Icons, ApplicantNumber...
  Data/          ServisyoDbContext, AdminSeeder
tests/Servisyo.Tests/   <Feature>Tests.cs, one per service
docs/                   <feature>-notes.md (design notes) + dev SQL
Servisyo/               Obsidian vault (personal notes / second brain)
graphify-out/           knowledge graph (graph.json, GRAPH_REPORT.md, graph.html)
```

## Conventions (match existing code)
- **Services return outcome records**, not exceptions: `enum XOutcome { Success, NotFound, NotPending, Invalid, NotAllowed }` + `record XResult(Outcome, Message)`. Controllers switch on the outcome.
- **Never trust the browser for identity.** Admin/user id comes from `ClaimTypes.NameIdentifier`; services re-check role + `AccountStatus == Active` in the DB. `NotAllowed` → controller signs the user out.
- **Provider gating:** every provider page maps `ProviderDashboardAccess` (Allowed / Pending → `Account/PendingApproval` / Rejected → `ApplicationNotApproved` / else sign out) via a `Denied(...)` helper.
- **Status changes** go through one place (e.g. `AccountTransitions`) and use conditional updates (`WHERE Status = 'Pending'`) inside a transaction to survive double clicks; write an `AdminAction` log + a `Notification` like `ProviderApprovalService.DecideAsync`.
- Antiforgery is global (`AutoValidateAntiforgeryTokenAttribute`); POST forms still carry `[ValidateAntiForgeryToken]`.
- Feedback to the user: `TempData["Toast"]` + `TempData["ToastKind"]` ("ok"/"error"), then redirect (PRG).
- Times stored UTC; displayed Manila (UTC+8) as `"MMM d, yyyy"` with `CultureInfo.InvariantCulture`.
- Inject `TimeProvider` (and optional `ILogger`) for testability; tests use a fixed clock.
- Comments explain *why* in plain English; keep the existing density.
- New feature checklist: entity/migration → service + interface + DI line in `Program.cs` → controller → views → tests → `docs/<feature>-notes.md` → `graphify update .`

## Git
- Branch per feature (`feature/<name>`), PRs into `main`. Commit messages: imperative, one line summary (see `git log`).
- `graph.json` diffs are large by design — don't flag them.

## Second brain (where knowledge lives)
| Need | Go to |
|---|---|
| How code connects | `graphify query/explain/path`, then `graphify-out/GRAPH_REPORT.md` |
| Why a feature works this way | `docs/<feature>-notes.md` |
| Visual browse | `graphify-out/graph.html`, or open `graphify-out/` as an Obsidian vault |
| Past Q&A that helped | `graphify-out/memory/` (save with `graphify save-result ... --outcome useful`) |
| Personal notes | `Servisyo/` Obsidian vault |

When an answer took real digging, save it so the next session is cheaper:
`graphify save-result --question "<q>" --answer "<short answer>" --nodes <Node1> <Node2> --outcome useful`

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.
