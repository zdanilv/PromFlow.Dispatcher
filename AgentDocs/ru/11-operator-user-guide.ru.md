# Руководство оператора

Этот документ описывает использование функциональности Stage 1-14 в desktop
приложении. Аудитория - Administrator или commissioning engineer, который готовит
локальную установку PromFlow.Dispatcher.

## Первый запуск

Если база пользователей пустая, приложение открывает administrator bootstrap. Создайте
первого Administrator с паролем, который удовлетворяет секции `Authentication`. После
bootstrap войдите через login screen.

Workspace создается только после успешного login. Logout очищает process session и
dispose workspace content. Если Administrator отключает текущего пользователя, active
session отзывается, и следующее protected action получает deny.

## Users

Откройте вкладку `Users` под Administrator. Это permission-only recovery surface; она не
требует commercial license feature.

Во вкладке можно:

- обновить local user list;
- создать `User` или `Administrator`;
- включить или отключить пользователя;
- сменить пароль выбранного пользователя.

UI не показывает password hashes. Password fields очищаются после операций. Если
появился row version conflict, нажмите `Refresh` и повторите операцию.

Роли:

- `User`: просмотр Route Map и equipment commands.
- `Administrator`: administrative permissions, но без bypass commercial features.

## License

Откройте вкладку `License` под Administrator. Эта вкладка доступна для recovery даже без
valid commercial license.

Типовой flow:

1. Нажмите `Export request`, чтобы создать installation request для этой машины.
2. На trusted offline workstation выпустите license через issuer tool.
3. Нажмите `Install` и выберите signed `.promlicense`.
4. Проверьте status, dates, edition, organization, version range и features.

Примеры issuer:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key public.json --private-key private.pem
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile customer-profile.json --private-key private.pem --key-id production-key-2026-01 --out customer.promlicense
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license customer.promlicense --public-key public.json
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license customer.promlicense
```

Не commit private keys, generated licenses или installation requests.

## Configuration

Production deployment должен задать:

- `Authentication`: password policy и lockout settings.
- `Licensing.TrustedPublicKeys`: только public keys, без private key material.
- `Archive`: archive directory, export directory, retention/query/export bounds.
- `ModbusDemo` endpoint settings: host, port, unit id, mode и start addresses.
- `Modbus.DataMap`: physical PLC mapping для RouteMap `SignalId`.

`Modbus.DataMap` и `ModbusDemo.DataMap` разные. RouteMap UI использует `SignalId`; PLC
addresses живут в `Modbus.DataMap`.

## Workspace tabs

Видимые вкладки зависят от permissions и license features:

- `Route Map`: `ViewRouteMap` + `RouteMap`.
- `SignalId <-> Modbus`: `ViewSignalMapping` + `EngineeringTools`.
- `Modbus Demo`: `ViewModbusDiagnostics` + `Diagnostics`.
- `Archive`: `ViewArchive` + `Archive`.
- `License`: `ViewLicense`, без license feature.
- `Users`: `ManageUsers`, без license feature.

## RouteMap и commands

RouteMap используется для наблюдения domain equipment state и отправки разрешенных
equipment commands. Command delivery все равно зависит от PLC interlocks и final PLC
acceptance. Source of truth - readback. Во время shutdown ordinary non-emergency
commands отклоняются до physical writes; emergency delivery остается доступной.

`SignalId <-> Modbus` используется для связи domain signals с physical PLC mapping. Не
помещайте raw Modbus addresses прямо в RouteMap definitions или XAML.

## Archive

Откройте `Archive` под Administrator с license feature `Archive`. UI использует
server-side paging и cancellation, а не full-history loading.

Разделы:

- Snapshots: сначала metadata list, decoded coil/register details по выбору записи.
- Commands: semantic equipment command audit.
- Events: runtime и Modbus lifecycle events.
- Security Audit: authentication, authorization, license и user-management events.
- Maintenance: retention, export и backup actions.

Export требует `ArchiveExport`. Maintenance и backup требуют `Archive`.

## Lifecycle

Startup/shutdown централизованы. Coordinator инициализирует persistence, license state,
archive runtime, Modbus archive collector и Modbus autostart policy. Shutdown dispose
workspace, останавливает Modbus, отписывает collector, flush archive и освобождает
single-instance ownership.

## Troubleshooting

- Нет пользователей: выполните bootstrap.
- Login fails: проверьте username, disabled state, lockout и password policy.
- Нет commercial tabs: установите valid license с нужными features.
- Permission denied: войдите под ролью с нужным permission.
- License expired или wrong installation: выпустите и установите replacement license.
- Archive degraded: проверьте archive directory access, disk space и `ArchiveHealth`.
- Wrong values in RouteMap: проверьте `Modbus.DataMap`, address notation и readback.
- Transient SQLite test failure: повторите targeted test; known disposed-object archive
  transient проходил на rerun в acceptance evidence.
