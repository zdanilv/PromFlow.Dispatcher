# Offline License Guide

Licenses are signed offline JSON envelopes. The verifier checks exact decoded UTF-8
payload bytes with ECDSA P-256, SHA-256 and IEEE P1363 fixed-field signatures. Payloads
are not encrypted, so they must not contain secrets.

## Trusted Keys

The repository does not contain production private keys or production license files.
`Licensing.TrustedPublicKeys` is empty by default and must be supplied by deployment
configuration before customer licenses can validate.

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

Never commit private keys, generated `.promlicense` files or installation request
artifacts. Test keys are rejected in production unless tests explicitly opt in.

## Installation

Administrators install licenses through the License tab. Installation validates the full
license before replacing the current file. Invalid, expired, wrong-product or
wrong-installation licenses do not replace a previously valid license. Successful install
refreshes immutable cached license state and raises change notification.

## Feature Policy

`LicenseFeatureGate` reads `ILicenseStateAccessor.Current`. A missing or invalid license
allows only access requirements with no `RequiredLicenseFeature`. Non-null feature
requirements fail closed on missing, expired, wrong-version, wrong-installation or clock
rollback states. Administrator does not bypass this gate.

## Clock And Identity

Installation identity is a random 256-bit Base64Url value stored under the per-user
license directory. Trusted time state is local best-effort rollback detection, not an
online time authority. Operators should keep system time stable and investigate rollback
failures before replacing licenses.
