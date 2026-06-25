# Руководство по offline license

Лицензии являются signed offline JSON envelopes. Verifier проверяет точные decoded
UTF-8 payload bytes через ECDSA P-256, SHA-256 и IEEE P1363 fixed-field signatures.
Payload не шифруется, поэтому в нем не должно быть секретов.

## Trusted keys

В репозитории нет production private keys и production license files.
`Licensing.TrustedPublicKeys` пуст по умолчанию и должен передаваться deployment
configuration до проверки customer licenses.

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
        "Algorithm": "ECDSA-P256-SHA256",
        "PublicKeyPem": "deployment-supplied public key only",
        "IsTestKey": false
      }
    ]
  }
}
```

Нельзя commit private keys, generated `.promlicense` files или installation request
artifacts. Test keys rejected in production, если tests явно не включили opt-in.

## Installation

Administrator устанавливает license через License tab. Installation сначала валидирует
license полностью и только затем заменяет current file. Invalid, expired, wrong-product
или wrong-installation license не заменяет прежнюю валидную license. Успешная установка
обновляет immutable cached license state и поднимает change notification.

## Feature policy

`LicenseFeatureGate` читает `ILicenseStateAccessor.Current`. Missing или invalid license
разрешает только requirements без `RequiredLicenseFeature`. Non-null feature requirements
fail closed при missing, expired, wrong-version, wrong-installation или clock rollback.
Administrator не обходит этот gate.

## Clock и identity

Installation identity - случайное 256-bit Base64Url значение в per-user license
directory. Trusted time state является локальной best-effort rollback detection, а не
online time authority. Операторы должны держать system time стабильным и разбирать
rollback failures до замены license.
