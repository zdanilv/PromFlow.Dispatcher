# AGENTS.md — PromFlow.Dispatcher / Codex

## 1. Scope and source of truth

PromFlow.Dispatcher is a .NET 10, Avalonia 12/ReactiveUI industrial operator application. The solution is DesktopTemplate.slnx; Boot composes Application, Domain, Desktop and Infrastructure, with separate Modbus and OPC UA infrastructure. Keep dependencies pointing inward. Work on the user's current branch: check git branch --show-current, git status --short and the relevant diff before editing. Never assume a branch name from this file.

Current code, tests and configuration establish behavior. [The master index](AgentDocs/ru/00-master.ru.md) routes to current guides; [the code map](AgentDocs/ru/11-code-map.ru.md) locates owners and tests. AgentDocs/source/ru is historical reference. When sources disagree, verify the branch and identify drift; do not silently change established behavior. Read one relevant guide and targeted files first. Do not repeatedly read large guides, README or the whole solution without a concrete need.

## 2. Mandatory safety and compatibility

- The PLC is the final authority for control and safety logic. The UI, including user/admin visibility, is not an interlock or industrial authorization boundary. Never transfer PLC safety responsibility to the UI.
- Never send live PLC or field-device writes, start/stop/reset, alarm ACK or analogous commands; change actual site mappings; or connect to an external industrial endpoint without explicit authorization and an established safe environment. Use mocks, fixtures and loopback for development and tests.
- Preserve public APIs, user data, JSON compatibility and current UX unless the task explicitly changes them. Make small, reversible edits; avoid unrelated refactors, dependency upgrades, generated noise and global formatting.
- Readback and Good/Bad/Stale quality determine final control state. A snapshot/readback callback must not accidentally dispatch a command. Dispatch observable Avalonia changes on the UI thread. Never automatically retry a potentially non-idempotent command after timeout or connection loss; first establish actual state and the PLC contract. See [Route Map](AgentDocs/ru/02-route-map-guide.ru.md), [Modbus](AgentDocs/ru/03-modbus-tcp-guide.ru.md) and [test matrix](AgentDocs/ru/06-testing-and-diagnostics.ru.md).
- For Modbus command, address or mapping work, preserve SignalId separation, write ordering, readback, pulse duration and cancellation. Validate Direction, ValueType, area/offset/bit, client/server address bases, UnitId, Length, write mode and typed codec capacity. Register-bit read-modify-write must preserve neighboring bits. Alarm ACK is a bounded pulse, with repeat/clear tied to alarm state. See [Modbus](AgentDocs/ru/03-modbus-tcp-guide.ru.md) and [SignalId](AgentDocs/ru/04-signal-id-guide.ru.md).
- For OPC UA work, keep Application contracts separate from SDK infrastructure; check access, identity, certificate policy, typed tags, cancellation, bounded lifecycle, subscriptions, concurrency and disposal. Current None/Anonymous and disabled-certificate defaults are not a production security guarantee. Never downgrade a secured endpoint silently. Confirm what is exposed in Workspace before promising UI functionality. See [OPC UA](AgentDocs/ru/10-opc-ua-guide.ru.md).
- Preserve user-profile configuration and portable Windows/Linux paths; installed defaults and upgrades must not overwrite user settings. Protect imported/exported data; never log credentials. See [configuration locations](AgentDocs/ru/09-configuration-file-locations.ru.md).

## 3. Development workflow and documentation routing

1. Classify risk by behavior and physical effect. For high-risk changes, write a 3–7-line plan and test matrix before editing. Define the intended result; inspect existing edits and preserve them. Use rg, a relevant guide, the owner, immediate call sites and named tests. Do not infer behavior from documentation alone.
2. Edit only what the task needs. Preserve versioned Route Map JSON, migrations, validation, import/export and unknown user data. RouteMapConfigurationManager owns the mutable definition; do not install RouteMapDefinition as a fixed singleton. The schema version comes from RouteMapConfigurationDocument.CurrentSchemaVersion, not this file. See [Route Map](AgentDocs/ru/02-route-map-guide.ru.md).
3. Route Map uses domain SignalId; physical addresses belong in Modbus.DataMap. Modbus.AlarmMap is independent; ModbusDemo.DataMap is legacy/demo. system.fault is a PLC-mapped Read/Bool SignalId in Modbus.DataMap. ModbusDemo owns the shared TCP runtime lifecycle; do not add a separate Route Map transport or silent autostart. See [Modbus](AgentDocs/ru/03-modbus-tcp-guide.ru.md) and [SignalId](AgentDocs/ru/04-signal-id-guide.ru.md).
4. For Start/Stop, selector C, Enabled=false reset, TopBar system.reset, OffFeedback and equipment parameters, follow the exact ordering, access/write mode, readback and one-time dispatch rules in [Route Map](AgentDocs/ru/02-route-map-guide.ru.md) and [Modbus](AgentDocs/ru/03-modbus-tcp-guide.ru.md). Keep system.reset a Pulse, never Latched. Preserve offline local setpoints and at most one pending auto-send attempt after reconnect.
5. user mode exposes Route Map while admin exposes configuration. Hidden admin views must not disable configured runtime or alarms. Application.WorkMode comes from install-level boot settings; mutable settings live in the user profile. Confirm the actual Workspace tabs and integration in code. See [architecture](AgentDocs/ru/01-architecture-overview.ru.md) and [configuration](AgentDocs/ru/09-configuration-file-locations.ru.md).
6. Use [coding rules](AgentDocs/ru/05-coding-rules.ru.md) for UI threads, new-file visibility in Visual Studio, stable project IDs, Avalonia designer metadata and solution build after metadata edits. Use [icons](AgentDocs/ru/07-svg-icons-guide.ru.md) for Material Icons and embedded SVG. Use [build/install](AgentDocs/ru/08-build-and-install.ru.md) for Windows/ALT Linux packaging and case-sensitive Linux resources.

## 4. Agent orchestration

The configured orchestrator is GPT-6 Sol / high (.codex/config.toml). It owns scope, safety decisions, integration and the final report. One executor by default: implement a known small change directly. Delegate only when it saves total work, isolates a defined task or supplies independent evidence; parallelism itself does not save tokens. If a configured model/effort is unavailable, disclose the substitution and use the closest capable available model. Do not claim an unavailable model ran.

| Existing role | Use when |
|---|---|
| explorer (Luna/medium) | Read-only ownership or test lookup when the path is unclear. |
| implementer (GPT-6.1 Sol/medium) | Bounded, non-safety-critical C# feature with a verified contract and owned files. |
| avalonia_ui (GPT-6.1 Terra/medium) | Scoped AXAML, Route Map layout, icons or visual fix. |
| protocol_guard (GPT-6.1 Sol/high) | Modbus/OPC UA writes, readback, mapping, security, lifecycle or concurrency. |
| tester (Luna/medium) | Focused test discovery, clear regression assertions and local test execution. |
| reviewer (GPT-6.1 Sol/high) | One independent read-only review for safety-critical, protocol, schema, packaging, cross-layer or large changes. |
| release_checker (Luna/medium) | Read-only packaging and platform checks. |
| architect_escalation (Astra/medium) | Exception: an evidenced design, security or concurrency conflict remains unresolved after normal investigation. Never a default first pass. |

The role TOMLs under .codex/agents define actual model/effort assignments. The configured session limit is two subagents, excluding the orchestrator. Do not increase it casually or edit the same file/shared runtime in parallel. Give each agent a narrow question, 1–5 target paths, relevant guide, allowed files, read-only/edit boundary, safety restriction, proof and stop condition. Ask for at most about 12 lines of findings with paths and evidence, not file dumps. Do not duplicate research or repeat a failed cheap-agent attempt without escalation. Use one focused reviewer where the cost of error warrants it.

## 5. Verification

Verification is proportional and mandatory. For behavior changes, add or update meaningful regression tests; compile the affected project and run focused named tests first. Use [the testing guide](AgentDocs/ru/06-testing-and-diagnostics.ru.md) to choose projects and loopback cases. Route Map/SignalId changes need relevant unit and headless UI coverage; Modbus and OPC UA protocol changes need their protocol tests. For cross-layer, protocol, schema, DI, packaging or pre-merge changes, build and test the full solution; complete applicable platform smoke checks. Installer/RPM execution requires explicit authorization and the appropriate environment. For docs-only changes, check links and diff; a build is unnecessary without a concrete risk.

Restore DesktopTemplate.slnx once only if assets are missing or dependencies changed. Prefer --no-restore thereafter. On failure, inspect and rerun the smallest reproducer; do not blindly repeat the full suite. Never report a test as passed unless it ran. Report exact commands and PASS/FAIL/NOT RUN, including environmental skips.

## 6. Git and output contract

Preserve existing user edits and stay on the user's branch. No reset --hard, destructive clean, force push, commit, push, merge, package publication, installer execution or release-version change without an explicit request. Do not lose or overwrite user configuration. Before finishing, inspect git diff and git diff --check.

Report: result; changed files; safety/compatibility effects; exact checks and results; remaining risks, limitations and unmet requirements. Stop when acceptance criteria are met.
