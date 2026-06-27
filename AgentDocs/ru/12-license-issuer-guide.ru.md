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

## Как исправить `License key id is not trusted`

Ошибка `License key id is not trusted` с reason `UnknownKeyId` означает, что файл
`.promlicense` был найден и прочитан, но приложение не нашло его `KeyId` в настройке
`Licensing.TrustedPublicKeys`.

Простыми словами:

- license file подписан private key;
- приложение не должно знать private key;
- приложение должно знать public key;
- public key нужно добавить в `appsettings.json` до установки license.

### Проверка файлов из `license-work`

Откройте PowerShell в корне репозитория:

```powershell
Set-Location C:\Users\mrmsk\source\repos\PromFlow.Dispatcher
```

Проверьте, какие файлы есть для диагностики:

```powershell
Get-ChildItem .\license-work | Select-Object Name,Length,LastWriteTime
```

Ожидаемые файлы:

| Файл | Для чего нужен |
|---|---|
| `installation.promrequest` | request с целевой станции; из него берется `installationId` |
| `production-key-2026-01.public.json` | public key; его нужно добавить в `TrustedPublicKeys` |
| `production-key-2026-01.private.pem` | private key; нужен только для выпуска license, в приложение не вставляется |
| `full-package-1-day-profile.json` | profile, из которого была выпущена license |
| `full-package-1-day.promlicense` | готовая license, которую выбирают в desktop tab `License` |

Проверьте, что public key и license используют один и тот же key id:

```powershell
$public = Get-Content .\license-work\production-key-2026-01.public.json -Raw | ConvertFrom-Json
$license = Get-Content .\license-work\full-package-1-day.promlicense -Raw | ConvertFrom-Json
$public.KeyId
$license.KeyId
```

Ожидаемый вывод:

```text
production-key-2026-01
production-key-2026-01
```

Если значения разные, license выпущена не тем private key или выбрана не та license.
Выпустите license заново с `--key-id`, совпадающим с public key.

### Куда вставить public key

Если приложение запускается из репозитория, откройте:

```text
Configurator.Boot\appsettings.json
```

Если приложение уже собрано или установлено отдельно, откройте `appsettings.json`,
который лежит рядом с `Configurator.Boot.exe`. Desktop приложение читает configuration
из `AppContext.BaseDirectory`, то есть из папки, где лежит запущенный executable.

В секции `Licensing` замените пустой список:

```json
"TrustedPublicKeys": []
```

на список с вашим public key:

```json
"TrustedPublicKeys": [
  {
    "KeyId": "production-key-2026-01",
    "PublicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
    "IsTestKey": false
  }
]
```

Значения `KeyId`, `PublicKeyPem` и `IsTestKey` берутся из
`.\license-work\production-key-2026-01.public.json`. Private file
`production-key-2026-01.private.pem` сюда никогда не вставляется.

После изменения config:

1. Сохраните `appsettings.json`.
2. Полностью закройте приложение PromFlow Dispatcher.
3. Откройте приложение снова.
4. Войдите как Administrator.
5. Откройте tab `License`.
6. Нажмите `Install`.
7. Выберите `.\license-work\full-package-1-day.promlicense`.

Ожидаемый результат: status `Valid`, а reason пустой. Если снова показано
`UnknownKeyId`, значит приложение читает другой `appsettings.json` или было не полностью
перезапущено.

## Production license на весь пакет с ограничением по времени

Этот runbook описывает полный ручной выпуск production-лицензии на весь пакет ПО на
`1 день`, `30 дней` или `1 год`. "Весь пакет" означает edition `Professional` и все
текущие features: `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`,
`EngineeringTools`, `Diagnostics`.

### Предусловия

- На машине выпуска установлен .NET SDK, совместимый с решением.
- Команды вводятся в Windows PowerShell.
- Команды запускаются из repo root:
  `C:\Users\mrmsk\source\repos\PromFlow.Dispatcher`.
- На целевой станции пользователь Administrator уже открыл desktop tab `License` и
  экспортировал `.promrequest`.
- Из `.promrequest` нужно взять значение `installationId`; оно вставляется в переменную
  `$installationId`.
- Для production не используйте `--test-key` и не передавайте private key заказчику.

### Какие файлы будут созданы

В примерах используется рабочая папка `.\license-work`. В зависимости от выбранного
срока будут созданы следующие файлы:

| Файл | Назначение |
|---|---|
| `.\license-work\production-key-2026-01.public.json` | Public key для `Licensing.TrustedPublicKeys` и команды `verify` |
| `.\license-work\production-key-2026-01.private.pem` | Private key выпуска; хранить только offline |
| `.\license-work\full-package-1-day-profile.json` | Profile для лицензии на 1 день |
| `.\license-work\full-package-30-days-profile.json` | Profile для лицензии на 30 дней |
| `.\license-work\full-package-1-year-profile.json` | Profile для лицензии на 1 год |
| `.\license-work\full-package-1-day.promlicense` | Готовая лицензия на 1 день |
| `.\license-work\full-package-30-days.promlicense` | Готовая лицензия на 30 дней |
| `.\license-work\full-package-1-year.promlicense` | Готовая лицензия на 1 год |

Обычно для одного заказчика создается только один profile и один `.promlicense`,
соответствующий выбранному сроку.

### Шаг 1. Открыть PowerShell и перейти в repo root

Откройте PowerShell и выполните:

```powershell
Set-Location C:\Users\mrmsk\source\repos\PromFlow.Dispatcher
```

Проверьте, что вы находитесь в правильной папке:

```powershell
Test-Path .\DesktopTemplate.slnx
```

Ожидаемый результат:

```text
True
```

Если вывод `False`, команда запускается не из корня репозитория; повторите
`Set-Location` с правильным путем.

### Шаг 2. Создать рабочую папку

```powershell
New-Item -ItemType Directory -Force .\license-work | Out-Null
```

Ожидаемый результат: команда завершается без ошибки, а папка `.\license-work`
существует:

```powershell
Test-Path .\license-work
```

Ожидаемый вывод:

```text
True
```

### Шаг 3. Создать production key pair

Если production key pair уже существует, не создавайте новый ключ для того же key id.
Используйте private key из защищенного offline-хранилища и переходите к шагу 4.

Для нового production key pair выполните:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key .\license-work\production-key-2026-01.public.json --private-key .\license-work\production-key-2026-01.private.pem
```

Ожидаемый вывод:

```text
Key pair generated.
```

Ожидаемые файлы:

```powershell
Test-Path .\license-work\production-key-2026-01.public.json
Test-Path .\license-work\production-key-2026-01.private.pem
```

Ожидаемый вывод:

```text
True
True
```

Файл `.public.json` можно использовать в deployment configuration
`Licensing.TrustedPublicKeys`. Файл `.private.pem` нельзя копировать заказчику,
commit в git или хранить рядом с установленным приложением.

### Важно перед шагами 4-6

Шаги 4, 5 и 6 нужно выполнять в одном и том же окне PowerShell, не закрывая его между
шагами. Строки вида `$installationId`, `$durationDays`, `$licenseName`, `$profilePath`
и `$licensePath` - это переменные PowerShell. Это не файлы и не команды LicenseIssuer.

Если выполнить `Test-Path $profilePath` до шага 6, PowerShell покажет ошибку про
`NULL`, потому что переменная `$profilePath` еще не создана. Если открыть новое окно
PowerShell, все переменные тоже исчезнут. В таком случае просто повторите шаги 4, 5 и 6
сверху вниз в одном окне.

Если вы уже видите ошибку `Missing required option "--profile"`, не продолжайте
собирать команду `issue` вручную. Это почти всегда значит, что `$profilePath` пустой,
потому что шаг 6 не был выполнен в текущем окне PowerShell. Вернитесь к шагу 4 и
пройдите шаги 4, 5, 6 и 7 подряд.

### Шаг 4. Взять installationId из `.promrequest` и вставить его в PowerShell

1. Убедитесь, что файл запроса лежит в рабочей папке. В примере он называется
   `installation.promrequest`:

```powershell
$requestPath = ".\license-work\installation.promrequest"
Test-Path $requestPath
```

Ожидаемый вывод:

```text
True
```

Если вывод `False`, скопируйте файл `.promrequest`, экспортированный на целевой станции
через вкладку `License`, в папку `license-work` и переименуйте его в
`installation.promrequest`.

2. Откройте request file в Блокноте:

```powershell
notepad $requestPath
```

3. В Блокноте найдите строку с `installationId`. Она выглядит примерно так:

```json
"installationId": "PASTE-INSTALLATION-ID-FROM-PROMREQUEST"
```

4. Скопируйте только значение между кавычками после двоеточия. Не копируйте название
   поля `installationId`, двоеточие, кавычки и запятую.

5. Вернитесь в то же окно PowerShell и выполните команду ниже. Вместо
   `PASTE-INSTALLATION-ID-FROM-PROMREQUEST` вставьте настоящий ID из вашего файла:

```powershell
$installationId = "PASTE-INSTALLATION-ID-FROM-PROMREQUEST"
```

Пример того, как должна выглядеть команда после замены:

```powershell
$installationId = "C0k9iQ2iKfQ3qYt1nN4xexample"
```

6. Контрольная точка. Выполните:

```powershell
if ([string]::IsNullOrWhiteSpace($installationId) -or $installationId -eq "PASTE-INSTALLATION-ID-FROM-PROMREQUEST") {
  throw "STOP: installationId не вставлен. Вернитесь к шагу 4 и вставьте ID из .promrequest."
}

"OK: installationId вставлен: $installationId"
```

Ожидаемый вывод начинается так:

```text
OK: installationId вставлен:
```

Не переходите дальше, пока эта контрольная точка не прошла.

### Шаг 5. Выбрать срок действия лицензии

Оставайтесь в том же окне PowerShell. Выполните только один из трех блоков.

Для лицензии на 1 день:

```powershell
$durationDays = 1
$licenseName = "full-package-1-day"
```

Для лицензии на 30 дней:

```powershell
$durationDays = 30
$licenseName = "full-package-30-days"
```

Для лицензии на 1 год:

```powershell
$durationDays = 365
$licenseName = "full-package-1-year"
```

Контрольная точка:

```powershell
if ($null -eq $durationDays -or [string]::IsNullOrWhiteSpace($licenseName)) {
  throw "STOP: срок не выбран. Выполните один блок из шага 5."
}

"OK: срок выбран: $licenseName; дней: $durationDays"
```

Пример ожидаемого вывода для лицензии на 1 день:

```text
OK: срок выбран: full-package-1-day; дней: 1
```

Не вводите все три срока подряд. Если ввели несколько блоков, активным будет последний.

### Шаг 6. Создать profile JSON

Оставайтесь в том же окне PowerShell. Скопируйте и выполните весь блок целиком. Он:

- проверит, что шаг 4 и шаг 5 уже выполнены;
- задаст пути к файлам;
- создаст JSON profile;
- напечатает точные пути, которые будут использоваться на шаге 7.

В блоке можно заменить customer fields (`$customerFullName`, `$customerPhone`,
`$customerEmail`, `$organizationName`, `$siteAddress`) на реальные данные заказчика.

```powershell
if ([string]::IsNullOrWhiteSpace($installationId) -or $installationId -eq "PASTE-INSTALLATION-ID-FROM-PROMREQUEST") {
  throw "STOP: installationId не задан. Сначала выполните шаг 4."
}

if ($null -eq $durationDays -or [string]::IsNullOrWhiteSpace($licenseName)) {
  throw "STOP: срок не выбран. Сначала выполните шаг 5."
}

$keyId = "production-key-2026-01"
$privateKeyPath = ".\license-work\production-key-2026-01.private.pem"
$publicKeyPath = ".\license-work\production-key-2026-01.public.json"
$profilePath = ".\license-work\$licenseName-profile.json"
$licensePath = ".\license-work\$licenseName.promlicense"

$customerFullName = "Customer Contact"
$customerPhone = "+10000000000"
$customerEmail = "operator@example.invalid"
$organizationName = "Customer Organization"
$siteAddress = "Plant 1"

if (-not (Test-Path $privateKeyPath)) {
  throw "STOP: private key не найден: $privateKeyPath. Сначала выполните шаг 3."
}

if (-not (Test-Path $publicKeyPath)) {
  throw "STOP: public key не найден: $publicKeyPath. Сначала выполните шаг 3."
}

$validFromUtc = (Get-Date).ToUniversalTime()
$expiresAtUtc = $validFromUtc.AddDays($durationDays)

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
    fullName = $customerFullName
    phone = $customerPhone
    email = $customerEmail
  }
  organization = [ordered]@{
    name = $organizationName
    siteAddress = $siteAddress
  }
  installation = [ordered]@{
    bindingMode = "InstallationId"
    installationId = $installationId
  }
}

$profile | ConvertTo-Json -Depth 10 | Set-Content -Path $profilePath -Encoding utf8

if (-not (Test-Path $profilePath)) {
  throw "STOP: profile file не создан: $profilePath"
}

"OK: profile created: $profilePath"
"OK: license will be created here: $licensePath"
"OK: private key: $privateKeyPath"
"OK: public key: $publicKeyPath"
```

Ожидаемый вывод должен содержать строки:

```text
OK: profile created: .\license-work\full-package-1-day-profile.json
OK: license will be created here: .\license-work\full-package-1-day.promlicense
OK: private key: .\license-work\production-key-2026-01.private.pem
OK: public key: .\license-work\production-key-2026-01.public.json
```

Для 30 дней или 1 года имя будет другим:

```text
.\license-work\full-package-30-days-profile.json
.\license-work\full-package-1-year-profile.json
```

Дополнительная проверка содержимого profile:

```powershell
Get-Content $profilePath
```

Ожидаемый результат: PowerShell показывает JSON, где есть:

- `"product": "PromFlow.Dispatcher"`
- `"edition": "Professional"`
- `"minimum": "1.0.0"`
- `"maximumExclusive": "2.0.0"`
- все шесть features: `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`,
  `EngineeringTools`, `Diagnostics`
- `"bindingMode": "InstallationId"`
- `"installationId": "<ID целевой станции>"`

Если на этом шаге появилась ошибка `Cannot bind argument to parameter 'Path' because it
is null`, значит вы ввели проверочную команду из старой инструкции или перешли к
проверке до создания `$profilePath`. Повторите шаги 4, 5 и 6 в одном окне PowerShell.

### Шаг 7. Выпустить `.promlicense`

Сначала напечатайте пути, которые были созданы на шаге 6:

```powershell
"Profile: $profilePath"
"Private key: $privateKeyPath"
"License out: $licensePath"
```

Контрольная точка:

```powershell
if (-not (Test-Path $profilePath)) {
  throw "STOP: profile file не найден. Повторите шаг 6."
}

if (-not (Test-Path $privateKeyPath)) {
  throw "STOP: private key не найден. Повторите шаг 3."
}

if ([string]::IsNullOrWhiteSpace($licensePath) -or -not $licensePath.EndsWith(".promlicense")) {
  throw "STOP: licensePath должен быть путем к .promlicense файлу, а не папкой."
}
```

Важно: в `--out` должен быть путь к файлу, который заканчивается на `.promlicense`.
Не вводите `--out .\license-work`, потому что это папка, а не файл лицензии.

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile $profilePath --private-key $privateKeyPath --key-id $keyId --out $licensePath
```

Ожидаемый вывод:

```text
License issued: production-key-2026-01
```

Ожидаемый файл:

```powershell
Test-Path $licensePath
```

Ожидаемый вывод:

```text
True
```

При выбранных именах будет создан один из файлов:

```text
.\license-work\full-package-1-day.promlicense
.\license-work\full-package-30-days.promlicense
.\license-work\full-package-1-year.promlicense
```

### Шаг 8. Проверить подпись, срок и содержимое

Проверить подпись production public key:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license $licensePath --public-key $publicKeyPath
```

Ожидаемый вывод:

```text
License verification succeeded.
```

Посмотреть payload:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license $licensePath
```

Ожидаемый результат: JSON payload содержит:

- `validFromUtc`
- `expiresAtUtc`
- `edition`: `Professional`
- все шесть features: `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`,
  `EngineeringTools`, `Diagnostics`
- `installation.bindingMode`: `InstallationId`
- `installation.installationId`: ID целевой станции

`inspect` показывает содержимое, но не доказывает доверие. Доверие подтверждает только
успешный `verify`.

### Шаг 9. Подключить public key в конфиг приложения

До установки license приложение должно доверять public key, которым можно проверить
подпись. Если этот шаг пропустить, в UI появится ошибка:

```text
License key id is not trusted.
Reason: UnknownKeyId
```

Проверьте, что license и public key относятся друг к другу:

```powershell
$public = Get-Content .\license-work\production-key-2026-01.public.json -Raw | ConvertFrom-Json
$license = Get-Content $licensePath -Raw | ConvertFrom-Json
$public.KeyId
$license.KeyId
```

Ожидаемый вывод:

```text
production-key-2026-01
production-key-2026-01
```

Откройте `appsettings.json`, который использует приложение:

- при запуске из репозитория: `Configurator.Boot\appsettings.json`;
- при запуске собранного приложения: `appsettings.json` рядом с `Configurator.Boot.exe`.

Вставьте public key из `.\license-work\production-key-2026-01.public.json` в
`Licensing.TrustedPublicKeys`. Должно получиться так:

```json
"TrustedPublicKeys": [
  {
    "KeyId": "production-key-2026-01",
    "PublicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
    "IsTestKey": false
  }
]
```

Не вставляйте сюда `production-key-2026-01.private.pem`. Private key нужен только для
выпуска license.

После сохранения config полностью закройте приложение и откройте его снова. Только после
этого переходите к установке `.promlicense`.

### Шаг 10. Передать лицензию заказчику

Передавайте заказчику только созданный `.promlicense`:

```text
.\license-work\<licenseName>.promlicense
```

Нельзя передавать:

- `.\license-work\production-key-2026-01.private.pem`
- любые другие private `.pem`
- рабочие profile файлы, если они содержат персональные данные, не предназначенные для
  передачи

На целевой станции Administrator открывает desktop tab `License`, выбирает install и
устанавливает `.promlicense`. После установки ожидаемый статус - `Valid`. Если license
содержит весь пакет, после login и refresh должны быть доступны commercial surfaces,
для которых у пользователя есть permission: Route Map, commands, Archive, ArchiveExport,
EngineeringTools и Diagnostics.

### Шаг 11. Ожидаемый срок действия

Все даты в license payload хранятся и проверяются в UTC.

| Срок | Переменные | Правило |
|---|---|---|
| 1 день | `$durationDays = 1`, `$licenseName = "full-package-1-day"` | `expiresAtUtc = validFromUtc + 1 day` |
| 30 дней | `$durationDays = 30`, `$licenseName = "full-package-30-days"` | `expiresAtUtc = validFromUtc + 30 days` |
| 1 год | `$durationDays = 365`, `$licenseName = "full-package-1-year"` | `expiresAtUtc = validFromUtc + 365 days` |

Например, если `validFromUtc = 2026-07-01T10:00:00Z`, то:

| Срок | `expiresAtUtc` |
|---|---|
| 1 день | `2026-07-02T10:00:00Z` |
| 30 дней | `2026-07-31T10:00:00Z` |
| 1 год | `2027-07-01T10:00:00Z` |

### Ошибки при генерации и установке лицензии

| Ошибка | Что означает | Где проверить | Как исправить |
|---|---|---|---|
| `License key id is not trusted` / `UnknownKeyId` | Приложение не нашло `KeyId` license в `Licensing.TrustedPublicKeys`. | `.\license-work\production-key-2026-01.public.json`, `.promlicense`, `appsettings.json`. | Добавьте public key в `TrustedPublicKeys`, сохраните config и полностью закройте приложение перед повторной установкой. |
| `TestKeyRejected` | License или public key созданы как test key, а production verifier не принимает test keys. | Поле `IsTestKey` в public key JSON и наличие `--test-key` в истории команды `generate-key`. | Для production создайте key без `--test-key`; не используйте `--allow-test-key` в production. |
| `InvalidSignature` | License подписана другим private key или файл `.promlicense` был изменен руками. | Сравните `KeyId` в public key и license, затем выполните `verify`. | Не редактируйте signed envelope; исправьте profile и выпустите новую license командой `issue`. |
| `InstallationMismatch` | License выпущена для другой станции. | `installationId` в `.promrequest` и `installation.installationId` в profile/inspect output. | Скопируйте правильный `installationId` из request целевой станции и выпустите license заново. |
| `ProductVersionUnsupported` | Версия приложения вне диапазона `productVersion.minimum` / `maximumExclusive`. | `Configurator.Boot\appsettings.json` или deployment config: `Licensing.ProductVersion`; profile JSON. | Расширьте version range в profile или установите подходящую версию приложения, затем выпустите license заново. |
| `Expired` | `expiresAtUtc` уже прошел относительно UTC времени станции. | `inspect --license`, системное UTC-время станции. | Выпустите новую license с более поздним `expiresAtUtc`; проверьте часы Windows. |
| `NotYetValid` | `validFromUtc` находится в будущем относительно UTC времени станции. | `inspect --license`, системное UTC-время станции. | Дождитесь начала срока или выпустите license с текущим/прошлым `validFromUtc`; проверьте часы Windows. |
| `Missing required option "--profile"` | Команда `issue` запущена без значения `--profile`; обычно `$profilePath` пустой. | В PowerShell выполните `$profilePath` и `Test-Path $profilePath`. | Повторите шаги 4, 5 и 6 в одном окне PowerShell, затем запускайте шаг 7. |
| `Cannot bind argument to parameter 'Path' because it is null` | Переменная PowerShell для пути еще не создана или потеряна после открытия нового окна. | `$profilePath`, `$licensePath`, `$privateKeyPath`. | Не закрывайте PowerShell между шагами; повторите шаги 4-6 сверху вниз. |
| `License verification failed` | CLI `verify` не смог подтвердить license указанным public key. | Public key file, `.promlicense`, `KeyId`, test/production mismatch. | Убедитесь, что public key соответствует private key, которым выпускалась license; при сомнении выпустите license заново. |
| `FileTooLarge` | License file больше `Licensing.MaxLicenseFileBytes`. | Размер `.promlicense`; `MaxLicenseFileBytes` в config. | Выберите правильный `.promlicense`, не ZIP/JSON/profile; не редактируйте envelope вручную. |
| `InvalidJson` / `InvalidEnvelopeFormat` | Выбран не license envelope или JSON поврежден. | Откройте выбранный файл в Блокноте; проверьте расширение. | В `Install` выбирайте именно `.promlicense`, а не `.json`, `.promrequest` или `.pem`. |
| `ProductMismatch` | License выпущена для другого product. | Поле `product` в profile/inspect output; `Licensing.Product` в config. | В profile должно быть `product = "PromFlow.Dispatcher"`; выпустите license заново. |
| `ClockRollbackDetected` | Trusted time state считает, что время станции откатили назад. | UTC-время Windows и license status в UI. | Исправьте системное время; при необходимости выполните recovery по operations guide. |

## Safety checklist

- Храните private `.pem` files вне репозитория и вне customer machines.
- Не commit `.pem`, `.promlicense` или `.promrequest` artifacts.
- Используйте `--test-key` и `--allow-test-key` только в tests или lab.
- Payload не шифруется; считайте customer payload конфиденциальными business data.
- Держите system time стабильным; clock rollback может сделать license invalid до
  диагностики состояния.
