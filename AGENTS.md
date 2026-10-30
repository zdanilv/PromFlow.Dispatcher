# AGENTS.md — PromFlow.Dispatcher / Codex

> Scope: `zdanilv/PromFlow.Dispatcher`, branch `16-demo-v1`. Main orchestrator: **GPT-6 Sol, High** (`gpt-6-sol`, `high`). Goal: minimal **total task cost/usage** while preserving correctness, regression coverage and industrial-control safety. This file replaces, but preserves, the existing root `AGENTS.md` rules. The requested `AGENT.md` is a distribution copy; Codex automatically reads **`AGENTS.md`**.

## 1. Operational contract (always)

1. **Read only the relevant context.** Start with this file, `git status --short`, the user's task and the relevant subsection in the routing table below. Read the full `README.md` or broader architecture documents only when necessary. Prefer `rg`, `git diff`, file snippets and named tests over scanning the entire solution.
2. **Do not assume docs equal code.** The branch is authoritative. Confirm affected types, calls, tests and config from code before editing. State verified facts separately from hypotheses. The `AgentDocs/ru` guides are primary project guidance, `AgentDocs/source/ru` is detailed/historical reference.
3. **Small, reversible changes.** No unrelated refactors, global formatting, dependency upgrades, mass rewrites, auto-generated noise, unnecessary wrappers or speculative additions. Preserve public APIs, JSON compatibility and current UX unless explicitly requested.
4. **Safety first:** never issue live PLC/field-device writes, reset commands, start/stop commands, modify actual site mappings, or run against an external industrial endpoint unless explicitly authorized and the exact environment is established. Prefer mocks, loopback and test fixtures. UI is **not** a PLC safety interlock; never move interlock or safety responsibility from PLC into the UI.
5. **Verification is mandatory and proportional.** Add/update regression tests when behavior changes. Run focused test(s) first; full solution verification for cross-layer, protocol, config-schema, packaging and pre-merge work. Report exact executed commands and results. Never claim an unrun test passed.
6. **Git hygiene.** Work only on the user's branch; check for existing edits and preserve them. No `reset --hard`, destructive clean, force push, push/merge/commit, package publication, installer execution, or release version changes without explicit request.
7. **Finish concisely:** outcome; touched files; safety/compatibility implications; commands/results; remaining risk. Include relevant file paths and line references. Stop when acceptance criteria are met.

## 2. Model routing (explicit; orchestrator chooses)

The **orchestrator is fixed: `gpt-6-sol` / `high`**. High is the user's preference, but do not burn the orchestrator context on copy/paste scans or repetitive execution. Delegate only when it saves orchestrator work, isolates evidence or provides independent verification. Each spawned agent consumes additional tokens and limits; **parallelism is not inherently cheaper**.

| Agent / model / effort | Delegate WHEN | Do NOT delegate WHEN |
|---|---|---|
| `explorer` — **GPT-6 Luna**, `medium` | Read-only search: locate RouteMap/Modbus/OPC UA ownership, call graph, relevant tests, summarize a limited diff | A file and one call site already identify the change; do not duplicate orchestrator research |
| `implementer` — **GPT-6.1 Sol**, `medium` | Scoped, medium-complex C# work with explicit contract and owned files; nontrivial ReactiveUI, configuration or cross-layer implementation | Safety-critical I/O logic lacks verified requirements or ownership is unclear |
| `avalonia_ui` — **GPT-6.1 Terra**, `medium` | `.axaml` styling, visual layout, RouteMap geometry, icons, screenshot-backed UI reproduction and narrow UI fixes | PLC writes, runtime protocol correctness or safety decisions are the primary issue |
| `protocol_guard` — **GPT-6.1 Sol**, `high` | Modbus read/write/readback, register-bit RMW, address mapping, alarm ACK, selector/reset pulses; OPC UA session/security/subscriptions/commands, lifecycle and concurrency | Pure text/documentation changes or trivial UI styling |
| `tester` — **GPT-6 Luna**, `medium` | Focused test discovery, test additions with well-defined assertions, building and reporting real failures | PLC-connected or production-device tests; difficult root-cause analysis without escalation |
| `reviewer` — **GPT-6.1 Sol**, `high` | Independent **read-only** review for protocol, safety, changes spanning modules, schema migration, Release/packaging or a large diff | Trivial doc/format changes already validated; avoid duplicate full audits |
| `release_checker` — **GPT-6 Luna**, `medium` | Read-only verification of Windows/ALT Linux packaging instructions, scripts, files, checksum and test plan | Running installers, signing or publishing without explicit permission |
| `architect_escalation` — **GPT-6 Astra**, `medium` | **Exception only:** the usual agents cannot resolve a verified design conflict, subtle concurrency/security error or repeated protocol failure; supply a concise evidence packet | Default coding, routine review or first pass. Astra must not be launched automatically |

If a named model/effort is unavailable in the current Codex client, disclose it, select the nearest **available** model with suitable capabilities and retain the role’s safety/instructions. Never silently assume an unavailable model actually ran.

### Delegation decision (minimize quota)

- **One agent by default.** Do not spawn agents for a known single-file fix or short lookup. Orchestrator implements directly if delegation overhead exceeds expected benefit.
- **Two agents max by default** (excluding orchestrator), enforced by `.codex/config.toml`. If three independent workstreams truly justify a temporary third agent, explicitly change the configured limit first. Never parallel-edit the same file or a shared configuration/runtime. Parallel investigation may be read-only.
- Assign precisely: task/question; 1–5 target paths; must-read guide(s); allowed files; tests/acceptance criteria; **read-only vs edit**; stop condition; output cap.
- Agent output: **at most ~12 lines** unless actual code/patch is required: findings, paths:line, tests, uncertain points. Never relay entire file contents or dump CI logs to the orchestrator.
- Do not spawn agents just to restate documentation, duplicate an earlier scan, or review each other's notes. Once evidence is sufficient, stop further exploration.
- Escalate **Luna → GPT-6.1 Sol → Astra** only for observed difficulty (failed tests, unknown state transitions, conflicting evidence, safety/security risk), not by task length alone.
- The main GPT-6 Sol High agent alone owns final integration, decision-making and the final user report. A high-risk change gets one focused independent reviewer rather than a panel of duplicate reviewers.

## 3. Repository map / minimal reading plan

| Task | Start in code | Read only these project guides | Target tests |
|---|---|---|---|
| Startup, DI, `admin`/`user`, configuration | `Configurator.Boot/Program.cs`, `Configurator.Application/`, `Configurator.Desktop/Workspace/WorkspaceView*` | `AgentDocs/ru/01-architecture-overview.ru.md`, `09-configuration-file-locations.ru.md` | `Configurator.Tests.Unit`, `ApplicationOptionsTests`, `WorkspaceViewModelTests` |
| Route Map view, nodes, cards, TopBar, geometry | `Configurator.Desktop/Workspace/RouteMap/` | `02-route-map-guide.ru.md`, `05-coding-rules.ru.md` | `Configurator.Tests.Unit/RouteMap/`, `Configurator.Tests.RouteMap.Ui` |
| SignalId, bindings, point mapping | `Workspace/RouteMap/SignalMapping/`, `Configurator.Application/Services/Signals/` | `04-signal-id-guide.ru.md`, `03-modbus-tcp-guide.ru.md` | `RouteMapSignalRuntimeAndMappingTests`, Modbus mapping/validation tests |
| Modbus client/server, RMW, stale/readback | `Configurator.Infrastructure.Modbus/`, `Configurator.Application/Services/Modbus/` | `03-modbus-tcp-guide.ru.md`, `06-testing-and-diagnostics.ru.md` | `Configurator.Infrastructure.Modbus.Tests` |
| Alarms, ACK, notification/history | `Configurator.Desktop/Workspace/Alarms/`, `RouteMap/Services/RouteMapSessionJournal.cs` | `03-modbus-tcp-guide.ru.md`, `06-testing-and-diagnostics.ru.md` | `ModbusAlarmMonitorTests`, `ModbusAlarmMapValidatorTests`, `RouteMapSessionJournalTests` |
| OPC UA client/server, browsing, tags, runtime | `Configurator.Application/Services/OpcUa/`, `Configurator.Infrastructure.OpcUa/`, `Configurator.Desktop/Workspace/OpcUa/` | `01-architecture-overview.ru.md`, `05-coding-rules.ru.md` **and code/tests** (OPC UA documentation may not describe the current UI integration) | `Configurator.Infrastructure.OpcUa.Tests` |
| UI icons/SVG | `Configurator.Desktop/Assets/icons`, `Configurator.Desktop/App.axaml` | `07-svg-icons-guide.ru.md` | focused UI/headless tests + manual screenshot |
| Installer / publish / platform | `Configurator.Boot/Packaging/`, `.pubxml` | `08-build-and-install.ru.md`, `09-configuration-file-locations.ru.md` | build + full tests + platform-specific smoke check |

**Project layout:** .NET 10 (`global.json`: `10.0.300` with `latestPatch`), Avalonia 12 + ReactiveUI, solution `DesktopTemplate.slnx`; layers `Configurator.Boot`, `Configurator.Application`, `Configurator.Domain`, `Configurator.Desktop`, `Configurator.Infrastructure`, `Configurator.Infrastructure.Modbus`, `Configurator.Infrastructure.OpcUa`; separate Modbus/OPC UA/RouteMap/UI/unit tests. Keep dependencies pointing inward. **OPC UA services and tests exist, but the current `WorkspaceView.axaml` tabs are Route Map, SignalId ↔ Modbus, alarm manager and Modbus TCP; do not imply an OPC UA tab is already exposed in the Workspace.** Confirm actual integrations before promising them.

## 4. Non-negotiable project invariants

### RouteMap / SignalId

- RouteMap UI uses **domain SignalId**, never embedded PLC coil/register addresses. Mapping lives in `Modbus.DataMap`. `ModbusDemo.DataMap` is **legacy/demo only** and must never be repurposed as production RouteMap mapping. Operator alarms are separate in `Modbus.AlarmMap`. Special exception: `system.fault` is an ordinary PLC-mapped `Read/Bool` SignalId in `Modbus.DataMap`.
- `RouteMapConfigurationManager` owns the mutable current definition; do **not** register `RouteMapDefinition` as a fixed singleton. Persist/load/migrate through the established configuration classes. Current RouteMap schema in branch is **v16**: handle backward migration, versioned JSON, import/export, invalid input and round-trip tests; do not silently drop unknown user data.
- Readback and quality (good/bad/stale) drive final UI state. Modbus callbacks must not directly mutate Avalonia state; dispatch observable UI changes on the UI thread. A readback/snapshot update must **not** accidentally issue a new PLC command.
- `admin` exposes mapping/configuration while `user` exposes Route Map only; nonvisible admin controls must not disable configured runtime/alarm monitoring. `Application.WorkMode` comes from install-level boot settings; mutable settings are user-profile files, not the installed defaults.

### PLC commands / Modbus

- Keep `StartCommand`/`StopCommand` mutually exclusive, ordered writes (`false` to opposite, then `true` to selected); preserve `StartOffFeedback`/`StopOffFeedback` read-only disable semantics. Card selector `С` uses distinct `UncheckedCommand`/`CheckedCommand` `ReadWrite/Bool` **Latched** points, with correct write ordering and readback. Preserve special `Enabled=false` reset behavior and never repeat reset for every identical snapshot.
- TopBar `СБРОС`: `system.reset`, `ResetCommand`, `ReadWrite/Bool/**Pulse**`; do not change to Latched. Check pulse duration, timeouts, write ordering, cancellation, connection loss and readback. **Never automatically retry potentially non-idempotent commands.**
- Validate `Direction`, `ValueType`, area/offset/bit, client/server address bases, UnitId, length and write mode. Register-bit writes require safe read-modify-write semantics; test neighboring bits. Validate typed `WORD`/`DWORD`/`DATE`/strings against existing codecs and address capacities.
- `ModbusDemo` owns shared TCP runtime lifecycle. Do not invent an independent RouteMap transport or silently enable autostart. `Modbus.AlarmMap` covers alarm/ack/repeat independently of SignalId mapping. Alarm acknowledgment is a bounded pulse; check duplicate notifications and clearing only after alarm inactive.
- Equipment-card editable parameters remain in RouteMap definition/profile with persisted setpoint, pending-auto-dispatch and last error. Only one auto-send attempt after reconnect; do not create silent endless retries. `Н` remains locally usable offline as documented.

### OPC UA / cross-platform / security

- OPC UA application contracts stay in `Configurator.Application/Services/OpcUa`; client/server/runtime, subscriptions and address-space work stay in `Configurator.Infrastructure.OpcUa`. Preserve bounded connection lifecycle, cancellation, clean disposal, subscriptions and thread safety; verify typed tag validation and read/write access.
- Existing OPC UA DI uses `DisabledOpcUaSecurityProvider` and `AnonymousOpcUaIdentityProvider` for current None/Anonymous behavior. Do not claim secure production deployment by default; security changes require explicit requirements, threat assessment and protocol tests. Never silently downgrade an authenticated/secured endpoint.
- Keep user configuration portable: Windows `%LOCALAPPDATA%\\Configurator`, Linux `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator`. Protect imported/exported configs, avoid hard-coded machine paths or logging credentials. Installer updates must not overwrite user profile configuration.
- Preserve Windows 10/11 and ALT Linux 11.1 packaging where applicable. Prefer official guide over inferred platform steps; verify Linux case-sensitive resource paths.

### UI, assets and Visual Studio visibility (preserved existing AGENTS.md rules)

- Use Material Icons for standard UI symbols and embedded SVG resources for unique/multicolor icons; follow `07-svg-icons-guide.ru.md`. Do not add redundant icon copies or theme-breaking hard-coded colors.
- After creating/moving/generating files or folders, ensure they are **visible in Visual Studio Solution Explorer**. Add new projects to `DesktopTemplate.slnx` with **stable project Id** values. For SDK-style projects do not rely solely on implicit glob inclusion when visibility may be unclear; add explicit `<Folder Include="...\\" />` where useful. For new Avalonia XAML views add designer metadata `<None Update="...axaml"><SubType>Designer</SubType></None>` when surrounding project follows that pattern.
- After solution/project metadata changes run `dotnet build DesktopTemplate.slnx --no-restore`. If build is blocked by a running `.NET Host`, stop the app/debug session first; do not alter unrelated build artifacts to workaround locking.

## 5. Tests: smallest sufficient first

Use repository root; do `dotnet restore DesktopTemplate.slnx` **only when assets are missing or dependencies changed**. With assets restored:

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.OpcUa.Tests\Configurator.Infrastructure.OpcUa.Tests.csproj --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

- **Single file / UI-only:** compile affected project (and dependencies), then relevant named tests; visual UI tasks require screenshot/headless validation when possible.
- **RouteMap/SignalId:** relevant `Configurator.Tests.Unit/RouteMap` and `Configurator.Tests.RouteMap.Ui`; regression checks for v16 definition/import/export, bindings, stale/quality and command transitions.
- **Modbus:** relevant Modbus tests (mapping, codecs, register-bit writes, lifetime, server/client, alarms). Test on loopback/mock; no external PLC.
- **OPC UA:** `Configurator.Infrastructure.OpcUa.Tests` unit and controlled integration tests; integration may bind local ports and require an available loopback network. Record environmental skips/failures accurately.
- **DI/config/packaging/merge:** full solution build and tests; installer/RPM execution only when explicitly requested and correct OS/dependencies available.
- If `--no-restore` fails for missing assets, restore once, not in every test loop. On a failed check, inspect the relevant error and rerun the minimum reproducer; avoid repeating the full suite blindly.

## 6. Task workflow / acceptance

1. **Classify:** `small | medium | high-risk` by potential behavior and physical effect, not number of lines. Identify relevant docs and tests; define a measurable outcome.
2. **Locate:** use `rg` and targeted reads; delegate `explorer` *only if the ownership is unclear*.
3. **Choose:** small change → orchestrator directly; scoped UI → `avalonia_ui`; scoped feature → `implementer`; Modbus/OPC UA writes or lifecycle → `protocol_guard`; clear test additions → `tester`; unresolved architecture/security → `architect_escalation` after evidence.
4. **Implement:** smallest patch, owned files, no conflicting parallel editing. For high risk, first produce a 3–7-line plan and test matrix, then modify.
5. **Validate:** focused tests, then broader tests if scope dictates. Verify absence of unexpected configuration, mapping, lifecycle, UX and safety regressions.
6. **Review:** invoke `reviewer` for high-risk or cross-layer changes **once**; feed only diff, acceptance criteria and relevant tests. Resolve real defects; avoid style loops.
7. **Report:** `Result | Files | Tests (PASS/FAIL/NOT RUN) | Risks | Next action`. No fabricated success, savings statistics or performance claims.

### Standard delegation prompt (fill only needed fields)

```text
Role: <named agent> (<model>, <effort>).
Task / expected behavior: <specific change or question>.
Scope (allowed files): <paths>; do not touch anything else.
Read: <specific guide sections and symbols only>.
Safety: no live device communication; no destructive changes.
Proof: <tests/acceptance criteria>.
Return: <=12 lines: findings, changed paths, tests, unresolved items.
Stop after evidence is sufficient; do not start new subtasks.
```

### Budget / quality metrics

Do **not** invent fixed token multipliers across models. Compare the same real tasks by `input_tokens`, `cached_input_tokens`, `output_tokens`, `reasoning_output_tokens` when available; track final test pass rate, regressions, rework and Plus usage limits. Optimize **cost per accepted change**, not just tokens per response. If a cheaper agent repeatedly fails, escalate early and stop paying for repeated attempts.

## 7. Source of truth

- Repository: https://github.com/zdanilv/PromFlow.Dispatcher/tree/16-demo-v1
- `README.md` — usage and getting started; `AGENTS.md` — persistent development/agent constraints.
- `AgentDocs/ru/00-master.ru.md` — task-specific document index; detailed `01`–`09` documents are loaded **on demand**. In particular, use `02` RouteMap, `03` Modbus, `04` SignalId, `05` coding, `06` tests, `07` SVG, `08` installers, `09` config locations.
- If documentation, code and tests disagree: verify **current behavior in code/tests**, identify drift and propose documentation updates without changing established semantics silently.

