# Operator User Guide

This guide describes how to use the Stage 1-14 functionality in the desktop application.
It is written for an Administrator or commissioning engineer preparing a local
PromFlow.Dispatcher installation.

## First Start

When the application starts with an empty user database, it opens administrator
bootstrap. Create the first Administrator with a password that satisfies the configured
`Authentication` policy. After bootstrap, sign in through the login screen.

The workspace is created only after successful login. Logout clears the process session
and disposes workspace content. If the current user is disabled by an Administrator, the
active session is revoked and the next protected action is denied.

## Users

Open the `Users` tab as an Administrator. The tab is permission-only recovery surface and
does not require a commercial license feature.

Use it to:

- refresh the local user list;
- create a `User` or `Administrator`;
- enable or disable a user;
- change the selected user's password.

The UI never displays password hashes. Password fields are cleared after operations. If a
row version conflict appears, press `Refresh` and repeat the operation.

Roles:

- `User`: Route Map view and equipment commands only.
- `Administrator`: administrative permissions, but no automatic bypass of licensed
  commercial features.

## License

Open the `License` tab as an Administrator. This tab remains available for recovery even
when no valid commercial license is installed.

Typical flow:

1. Press `Export request` to create an installation request for this machine.
2. Use the issuer tool on a trusted offline workstation to issue a license.
3. Press `Install` and select the signed `.promlicense`.
4. Verify status, dates, edition, organization, version range and features.

Issuer examples:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key public.json --private-key private.pem
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile customer-profile.json --private-key private.pem --key-id production-key-2026-01 --out customer.promlicense
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license customer.promlicense --public-key public.json
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license customer.promlicense
```

Do not commit private keys, generated licenses or installation requests.

## Configuration

Production deployments should supply:

- `Authentication`: password policy and lockout settings.
- `Licensing.TrustedPublicKeys`: public keys only, no private key material.
- `Archive`: archive directory, export directory, retention/query/export bounds.
- `ModbusDemo` endpoint settings: host, port, unit id, mode and start addresses.
- `Modbus.DataMap`: physical PLC mapping for RouteMap `SignalId` values.

`Modbus.DataMap` and `ModbusDemo.DataMap` are separate. RouteMap UI uses `SignalId`; PLC
addresses stay in `Modbus.DataMap`.

## Workspace Tabs

Visible tabs depend on both permissions and license features:

- `Route Map`: `ViewRouteMap` + `RouteMap`.
- `SignalId <-> Modbus`: `ViewSignalMapping` + `EngineeringTools`.
- `Modbus Demo`: `ViewModbusDiagnostics` + `Diagnostics`.
- `Archive`: `ViewArchive` + `Archive`.
- `License`: `ViewLicense`, no license feature required.
- `Users`: `ManageUsers`, no license feature required.

## RouteMap And Commands

Use RouteMap to observe domain equipment state and issue permitted equipment commands.
Command delivery is still subject to PLC interlocks and final PLC acceptance. Readback is
the source of truth. During shutdown, ordinary non-emergency commands are rejected before
physical writes; emergency delivery remains available.

Use `SignalId <-> Modbus` to bind domain signals to physical PLC mapping. Do not put raw
Modbus addresses directly in RouteMap definitions or XAML.

## Archive

Open `Archive` as an Administrator with the `Archive` license feature. The UI uses
server-side paging and cancellation, not full-history loading.

Sections:

- Snapshots: metadata list first, decoded coil/register details on selection.
- Commands: semantic equipment command audit.
- Events: runtime and Modbus lifecycle events.
- Security Audit: authentication, authorization, license and user-management events.
- Maintenance: retention, export and backup actions.

Export requires `ArchiveExport`. Maintenance and backup require `Archive`.

## Lifecycle

Startup and shutdown are centralized. The coordinator initializes persistence, license
state, archive runtime, Modbus archive collector and Modbus autostart policy. Shutdown
disposes workspace, stops Modbus, unsubscribes collector, flushes archive and releases
single-instance ownership.

## Troubleshooting

- No users: complete bootstrap.
- Login fails: check username, disabled state, lockout and password policy.
- No commercial tabs: install a valid license with the required features.
- Permission denied: sign in as a role that has the required permission.
- License expired or wrong installation: issue and install a replacement license.
- Archive degraded: check archive directory access, disk space and `ArchiveHealth`.
- Wrong values in RouteMap: verify `Modbus.DataMap`, address notation and readback.
- Transient SQLite test failure: rerun the targeted test; known disposed-object archive
  transient has passed on rerun in acceptance evidence.
