# Руководство по Configurator.LicenseIssuer

`Configurator.LicenseIssuer` создает и проверяет offline-лицензии для
`PromFlow.Dispatcher`. Это отдельный CLI-проект; desktop-приложению не нужен ключ
выпуска. Лицензия хранится как подписанный JSON envelope с Base64Url payload. Нельзя
редактировать готовый `.promlicense` напрямую: любое изменение payload ломает подпись.

Все команды ниже запускаются из корня репозитория.

## Команды

Показать справку:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- --help
```

Создать ECDSA P-256 key pair:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key .\license-work\production-key-2026-01.public.json --private-key .\license-work\production-key-2026-01.private.pem
```

Создать test-only key pair:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id test-key-2026-01 --public-key .\license-work\test-key.public.json --private-key .\license-work\test-key.private.pem --test-key
```

Выпустить лицензию из profile:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile .\license-work\customer-profile.json --private-key .\license-work\production-key-2026-01.private.pem --key-id production-key-2026-01 --out .\license-work\customer.promlicense
```

Проверить production-лицензию:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license .\license-work\customer.promlicense --public-key .\license-work\production-key-2026-01.public.json
```

Проверить test-key лицензию в лаборатории:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license .\license-work\customer.promlicense --public-key .\license-work\test-key.public.json --allow-test-key
```

Посмотреть unsigned payload для диагностики:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license .\license-work\customer.promlicense
```

`inspect` только декодирует payload. Это не проверка доверия. Для проверки доверия
используйте `verify`.

## Exit codes

| Code | Значение |
|---:|---|
| `0` | Успех |
| `1` | Ошибка использования, отсутствующая или повторяющаяся option |
| `2` | Ошибка profile, key, license или JSON parse |
| `3` | Verification failed |
| `4` | Ошибка I/O или доступа |

## Полная форма customer-profile.json

Issuer читает `customer-profile.json` и превращает его в payload лицензии. Все UTC даты
лучше задавать ISO-8601 timestamp с `Z`.

```json
{
  "licenseId": "22222222-2222-2222-2222-222222222222",
  "product": "PromFlow.Dispatcher",
  "issuedAtUtc": "2026-06-24T12:00:00Z",
  "validFromUtc": "2026-07-01T00:00:00Z",
  "expiresAtUtc": "2027-07-01T00:00:00Z",
  "edition": "Professional",
  "licenseVersion": 1,
  "productVersion": {
    "minimum": "1.0.0",
    "maximumExclusive": "2.0.0"
  },
  "features": [
    "RouteMap",
    "RemoteControl",
    "Archive",
    "ArchiveExport",
    "EngineeringTools",
    "Diagnostics"
  ],
  "customer": {
    "fullName": "Customer Contact",
    "phone": "+10000000000",
    "email": "operator@example.invalid"
  },
  "organization": {
    "name": "Customer Organization",
    "siteAddress": "Plant 1"
  },
  "installation": {
    "bindingMode": "InstallationId",
    "installationId": "paste-installation-id-from-promrequest"
  }
}
```

Если `licenseId` пустой, issuer создаст новый ID. Если `issuedAtUtc` отсутствует,
issuer поставит текущее UTC время.

## Варианты profile

Professional license со всеми текущими commercial features:

```json
{
  "product": "PromFlow.Dispatcher",
  "validFromUtc": "2026-07-01T00:00:00Z",
  "expiresAtUtc": "2027-07-01T00:00:00Z",
  "edition": "Professional",
  "licenseVersion": 1,
  "productVersion": { "minimum": "1.0.0", "maximumExclusive": "2.0.0" },
  "features": [
    "RouteMap",
    "RemoteControl",
    "Archive",
    "ArchiveExport",
    "EngineeringTools",
    "Diagnostics"
  ],
  "customer": { "fullName": "Customer Contact", "phone": "", "email": "" },
  "organization": { "name": "Customer Organization", "siteAddress": "Plant 1" },
  "installation": { "bindingMode": "InstallationId", "installationId": "paste-installation-id" }
}
```

Community RouteMap-only license:

```json
{
  "product": "PromFlow.Dispatcher",
  "validFromUtc": "2026-07-01T00:00:00Z",
  "expiresAtUtc": "2027-07-01T00:00:00Z",
  "edition": "Community",
  "licenseVersion": 1,
  "productVersion": { "minimum": "1.0.0", "maximumExclusive": "2.0.0" },
  "features": [ "RouteMap" ],
  "customer": { "fullName": "Customer Contact", "phone": "", "email": "" },
  "organization": { "name": "Customer Organization", "siteAddress": "Plant 1" },
  "installation": { "bindingMode": "InstallationId", "installationId": "paste-installation-id" }
}
```

License, привязанная к installation:

```json
"installation": {
  "bindingMode": "InstallationId",
  "installationId": "paste-installation-id-from-exported-promrequest"
}
```

Unbound license для лаборатории или демо:

```json
"installation": {
  "bindingMode": "None",
  "installationId": ""
}
```

Trial/date-limited license:

```json
"validFromUtc": "2026-07-01T00:00:00Z",
"expiresAtUtc": "2026-08-01T00:00:00Z"
```

License, ограниченная версией продукта:

```json
"productVersion": {
  "minimum": "1.0.0",
  "maximumExclusive": "1.1.0"
}
```

Чтобы изменить лицензию, измените profile, выпустите новый `.promlicense`, проверьте его
и установите через License tab. Не исправляйте существующий signed envelope вручную.

## Features и editions

Известные feature names:

```text
RouteMap
RemoteControl
Archive
ArchiveExport
EngineeringTools
Diagnostics
```

`Community` разрешает только `RouteMap`. `Professional` разрешает все известные features.
Unknown feature strings, невалидные enum values или feature, недоступная в edition,
приведут к validation failure.

Workspace использует и permissions, и license features:

| Feature | Что открывает |
|---|---|
| `RouteMap` | Route Map workspace tab |
| `RemoteControl` | RouteMap equipment commands |
| `Archive` | Archive tab, archive queries, retention и backup |
| `ArchiveExport` | Archive export |
| `EngineeringTools` | SignalId-Modbus mapping и protected Modbus config changes |
| `Diagnostics` | Modbus Demo diagnostics tab |

Administrator может открыть recovery surfaces, например License и Users, без commercial
feature, но Administrator не обходит licensed commercial features.

## Installation request и install flow

На целевой станции войдите как Administrator, откройте `License` и экспортируйте
installation request. JSON `.promrequest` содержит `installationId`. Скопируйте этот ID
в profile, если `bindingMode` равен `InstallationId`.

После выпуска лицензии:

1. Выполните `verify`.
2. Скопируйте на целевую станцию только `.promlicense`.
3. Откройте `License` в desktop-приложении.
4. Установите файл.
5. Убедитесь, что статус `Valid` и появились ожидаемые workspace tabs.

## Deployment snippet для public key

В deployment configuration добавляйте только public key. Generated public key JSON
содержит те же поля, что используются ниже.

```json
{
  "Licensing": {
    "Product": "PromFlow.Dispatcher",
    "ProductVersion": "1.0.0",
    "LicenseFileName": "current.promlicense",
    "AllowedClockSkewMinutes": 5,
    "BindingMode": "InstallationId",
    "MaxLicenseFileBytes": 65536,
    "TrustedPublicKeys": [
      {
        "KeyId": "production-key-2026-01",
        "PublicKeyPem": "paste public key PEM from production-key-2026-01.public.json",
        "IsTestKey": false
      }
    ]
  }
}
```

## Production license на весь пакет с ограничением по времени

Используйте этот рецепт, когда заказчику нужно выдать весь текущий пакет ПО на 1 день,
30 дней или 1 год. "Весь пакет" означает edition `Professional` со всеми известными
features: `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`, `EngineeringTools` и
`Diagnostics`.

1. Создайте или используйте существующую production key pair. Для production не
   используйте `--test-key`:

```powershell
New-Item -ItemType Directory -Force .\license-work | Out-Null
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key .\license-work\production-key-2026-01.public.json --private-key .\license-work\production-key-2026-01.private.pem
```

Если production key уже существует, пропустите этот шаг и используйте существующий
private key из защищенного offline-хранилища.

2. На целевой станции экспортируйте installation request через desktop tab `License` и
   скопируйте `installationId`.

3. Выберите длительность:

```powershell
# Production license на 1 день
$durationDays = 1
$licenseName = "full-package-1-day"

# Production license на 30 дней
$durationDays = 30
$licenseName = "full-package-30-days"

# Production license на 1 год
$durationDays = 365
$licenseName = "full-package-1-year"
```

4. Создайте profile для выбранной длительности. Перед выпуском замените
   `$installationId` и customer fields.

```powershell
$keyId = "production-key-2026-01"
$privateKeyPath = ".\license-work\production-key-2026-01.private.pem"
$publicKeyPath = ".\license-work\production-key-2026-01.public.json"
$installationId = "paste-installation-id-from-promrequest"
$validFromUtc = (Get-Date).ToUniversalTime()
$expiresAtUtc = $validFromUtc.AddDays($durationDays)
$profilePath = ".\license-work\$licenseName-profile.json"
$licensePath = ".\license-work\$licenseName.promlicense"

$profile = [ordered]@{
  product = "PromFlow.Dispatcher"
  issuedAtUtc = $validFromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
  validFromUtc = $validFromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
  expiresAtUtc = $expiresAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
  edition = "Professional"
  licenseVersion = 1
  productVersion = [ordered]@{
    minimum = "1.0.0"
    maximumExclusive = "2.0.0"
  }
  features = @(
    "RouteMap",
    "RemoteControl",
    "Archive",
    "ArchiveExport",
    "EngineeringTools",
    "Diagnostics"
  )
  customer = [ordered]@{
    fullName = "Customer Contact"
    phone = "+10000000000"
    email = "operator@example.invalid"
  }
  organization = [ordered]@{
    name = "Customer Organization"
    siteAddress = "Plant 1"
  }
  installation = [ordered]@{
    bindingMode = "InstallationId"
    installationId = $installationId
  }
}

$profile | ConvertTo-Json -Depth 10 | Set-Content -Path $profilePath -Encoding utf8
```

5. Выпустите, проверьте и inspect license:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile $profilePath --private-key $privateKeyPath --key-id $keyId --out $licensePath
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license $licensePath --public-key $publicKeyPath
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license $licensePath
```

6. Скопируйте на целевую станцию только `$licensePath` и установите его через desktop
   tab `License`. Private key оставьте offline.

Итоговые интервалы действия:

| Длительность | `$durationDays` | Пример, если license issued at `2026-07-01T10:00:00Z` |
|---|---:|---|
| 1 день | `1` | Expires at `2026-07-02T10:00:00Z` |
| 30 дней | `30` | Expires at `2026-07-31T10:00:00Z` |
| 1 год | `365` | Expires at `2027-07-01T10:00:00Z` |

## Safety checklist

- Храните private `.pem` files вне репозитория и вне customer machines.
- Не commit `.pem`, `.promlicense` или `.promrequest` artifacts.
- Используйте `--test-key` и `--allow-test-key` только в tests или lab.
- Payload не шифруется; считайте customer payload конфиденциальными business data.
- Держите system time стабильным; clock rollback может сделать license invalid до
  диагностики состояния.
