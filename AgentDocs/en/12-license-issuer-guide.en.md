# Configurator.LicenseIssuer Guide

`Configurator.LicenseIssuer` creates and checks offline license files for
`PromFlow.Dispatcher`. It is a separate CLI project; the desktop application never needs
the issuer private key. A license is a signed JSON envelope with a Base64Url payload. Do
not edit a generated `.promlicense` file directly, because any payload change invalidates
the signature.

Run all examples from the repository root.

## Commands

Show help:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- --help
```

Create an ECDSA P-256 key pair:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key .\license-work\production-key-2026-01.public.json --private-key .\license-work\production-key-2026-01.private.pem
```

Create a test-only key pair:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id test-key-2026-01 --public-key .\license-work\test-key.public.json --private-key .\license-work\test-key.private.pem --test-key
```

Issue a license from a profile:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile .\license-work\customer-profile.json --private-key .\license-work\production-key-2026-01.private.pem --key-id production-key-2026-01 --out .\license-work\customer.promlicense
```

Verify a production license:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license .\license-work\customer.promlicense --public-key .\license-work\production-key-2026-01.public.json
```

Verify a test-key license in a lab:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license .\license-work\customer.promlicense --public-key .\license-work\test-key.public.json --allow-test-key
```

Inspect the unsigned payload for troubleshooting:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license .\license-work\customer.promlicense
```

`inspect` only decodes the payload. It does not prove that the license is trusted. Use
`verify` for trust.

## Exit Codes

| Code | Meaning |
|---:|---|
| `0` | Success |
| `1` | Usage error, missing option or duplicate option |
| `2` | Profile, key, license or JSON parse error |
| `3` | Verification failed |
| `4` | I/O or access error |

## Full Profile Shape

The issuer reads `customer-profile.json` and converts it to the license payload. All UTC
date fields should use ISO-8601 `Z` timestamps.

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

If `licenseId` is empty, the issuer generates a new ID. If `issuedAtUtc` is omitted, the
issuer uses the current UTC time.

## Profile Variants

Professional license with every current commercial feature:

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

Installation-bound license:

```json
"installation": {
  "bindingMode": "InstallationId",
  "installationId": "paste-installation-id-from-exported-promrequest"
}
```

Unbound license for lab or demo machines:

```json
"installation": {
  "bindingMode": "None",
  "installationId": ""
}
```

Trial or date-limited license:

```json
"validFromUtc": "2026-07-01T00:00:00Z",
"expiresAtUtc": "2026-08-01T00:00:00Z"
```

Product-version-limited license:

```json
"productVersion": {
  "minimum": "1.0.0",
  "maximumExclusive": "1.1.0"
}
```

Reissue or change a license by editing the profile, issuing a new `.promlicense`,
verifying it, and installing it through the desktop License tab. Do not patch an existing
license envelope.

## Feature And Edition Rules

Known feature names are:

```text
RouteMap
RemoteControl
Archive
ArchiveExport
EngineeringTools
Diagnostics
```

`Community` allows only `RouteMap`. `Professional` allows all known features. Unknown
feature strings, invalid enum values or a feature that the edition does not allow produce
a validation failure.

Workspace behavior uses both role permissions and license features:

| Feature | Unlocks |
|---|---|
| `RouteMap` | Route Map workspace tab |
| `RemoteControl` | RouteMap equipment commands |
| `Archive` | Archive tab, archive queries, retention and backup |
| `ArchiveExport` | Archive export |
| `EngineeringTools` | SignalId-Modbus mapping and protected Modbus config changes |
| `Diagnostics` | Modbus Demo diagnostics tab |

Administrator can open recovery surfaces such as License and Users without a commercial
feature, but Administrator does not bypass licensed commercial features.

## Installation Request And Install Flow

On the target workstation, log in as Administrator, open `License`, and export the
installation request. The `.promrequest` JSON contains the `installationId`. Copy that ID
into the profile when `bindingMode` is `InstallationId`.

After issuing the license:

1. Run `verify`.
2. Copy only the `.promlicense` to the target workstation.
3. Open `License` in the desktop application.
4. Install the file.
5. Confirm that the status is `Valid` and that expected workspace tabs appear.

## Deployment Public Key Snippet

Put only the public key into deployment configuration. The generated public key JSON has
the same fields used below.

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

## Production Full-Package Time-Limited Licenses

Use this recipe when the customer must receive the whole current software package for
exactly 1 day, 30 days or 1 year. "Full package" means `Professional` edition with every
known feature: `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`,
`EngineeringTools` and `Diagnostics`.

1. Create or reuse the production key pair. Do not use `--test-key` for production:

```powershell
New-Item -ItemType Directory -Force .\license-work | Out-Null
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- generate-key --key-id production-key-2026-01 --public-key .\license-work\production-key-2026-01.public.json --private-key .\license-work\production-key-2026-01.private.pem
```

If the production key already exists, skip this step and reuse the existing private key
from secure offline storage.

2. Export an installation request from the target workstation through the desktop
   `License` tab and copy its `installationId`.

3. Choose the duration:

```powershell
# 1-day production license
$durationDays = 1
$licenseName = "full-package-1-day"

# 30-day production license
$durationDays = 30
$licenseName = "full-package-30-days"

# 1-year production license
$durationDays = 365
$licenseName = "full-package-1-year"
```

4. Create the profile for the chosen duration. Replace `$installationId` and customer
   fields before issuing.

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

5. Issue, verify and inspect the license:

```powershell
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- issue --profile $profilePath --private-key $privateKeyPath --key-id $keyId --out $licensePath
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- verify --license $licensePath --public-key $publicKeyPath
dotnet run --project .\Configurator.LicenseIssuer\Configurator.LicenseIssuer.csproj -- inspect --license $licensePath
```

6. Copy only `$licensePath` to the target workstation and install it through the
   desktop `License` tab. Keep the private key offline.

The resulting validity windows are:

| Duration | `$durationDays` | Example result if issued at `2026-07-01T10:00:00Z` |
|---|---:|---|
| 1 day | `1` | Expires at `2026-07-02T10:00:00Z` |
| 30 days | `30` | Expires at `2026-07-31T10:00:00Z` |
| 1 year | `365` | Expires at `2027-07-01T10:00:00Z` |

## Safety Checklist

- Keep private `.pem` files outside the repository and outside customer machines.
- Do not commit `.pem`, `.promlicense` or `.promrequest` artifacts.
- Use `--test-key` and `--allow-test-key` only in tests or labs.
- Store customer payloads as confidential business data even though they are not
  encrypted.
- Keep workstation time stable; clock rollback can invalidate a license until the state
  is investigated.
