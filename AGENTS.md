# AGENTS.md — PromFlow.Dispatcher

## Scope

This repository is an industrial desktop application built with C#/.NET 10, Avalonia 12,
ReactiveUI, Microsoft DI, Modbus TCP, and OPC UA.

For archive, authorization, or licensing work, read:

1. `AgentDocs/en/00-master.en.md`
2. `AgentDocs/implementation/00-implementation-master.md`
3. `AgentDocs/implementation/PROGRESS.md`
4. Task-specific documents referenced by the selected implementation stage.

Work on exactly one implementation stage at a time.

## Non-negotiable invariants

- RouteMap UI uses domain `SignalId`, never raw Modbus addresses.
- Physical PLC addressing belongs to `Modbus.DataMap`.
- Never mix `Modbus.DataMap` and `ModbusDemo.DataMap`.
- `ModbusDemo` continues to own the shared Modbus endpoint and lifecycle.
- Modbus callbacks must never update Avalonia UI directly.
- Archive code must not depend on Avalonia, ReactiveUI, or ViewModels.
- Do not execute SQL in Modbus callbacks.
- Safety interlocks, emergency behavior, movement permissions, and final command acceptance remain in the PLC.
- Do not add hardcoded passwords, private license keys, production secrets, or debug bypasses.
- UI visibility is not authorization. Enforce permissions at service boundaries.
- A user role and a product license are independent authorization inputs.
- The administrator can recover and install a license without a user license, but must not automatically bypass licensed commercial features.
- Store timestamps in UTC.
- Keep queues bounded and shutdown deterministic.
- Emergency command delivery must not be blocked by archive unavailability.

## Working method

Before editing:

1. Run `git status --short`.
2. Read the relevant source and tests.
3. State the stage goal, proposed files, and risks.
4. Do not modify unrelated files.

During implementation:

- Prefer minimal, reviewable changes.
- Preserve public behavior unless the current stage explicitly changes it.
- Add tests with every behavior change.
- Avoid `async void` except framework event handlers.
- Propagate cancellation tokens.
- Do not block the UI thread with `.Wait()`, `.Result`, SQL, file I/O, or network I/O.
- Use immutable records at application boundaries.
- Explain every new production package and pin its version.
- Never use `BinaryFormatter`.
- Never serialize each Modbus register as a separate hot-path SQL row.

## Verification

Minimum:

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
git status --short
```

Modbus changes:

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

RouteMap changes:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

Persistence changes, after the project exists:

```powershell
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
```

## Definition of done

A stage is complete only when:

- its acceptance criteria are met;
- relevant build/tests pass;
- the diff is reviewed;
- no secret is present;
- `AgentDocs/implementation/PROGRESS.md` is updated;
- the next stage has not been started;
- the final response includes files, commands, results, limitations, and a commit message.
