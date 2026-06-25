# Authorization Guide

PromFlow authorization has two independent inputs: the authenticated user role and the
offline product license. Administrator permissions do not bypass commercial license
features. UI visibility is only a convenience; service boundaries must enforce access.

## Login And Session Lifecycle

Startup routes to administrator bootstrap when no users exist, otherwise to login. The
workspace is created only after successful authentication. Logout signs out through
`IAuthenticationService`, clears the current session and disposes workspace content.

Stage 14 closes the disabled-session gap. `IUserSessionAccessor.ClearIfCurrent(Guid)`
clears the in-process current session when `UserManagementService.SetUserEnabledAsync`
successfully disables that same user. Audit is best effort and must not keep a disabled
user authenticated.

## Permission Boundaries

Use `IAccessDecisionService` for service-boundary checks. Existing protected surfaces
include equipment commands, RouteMap configuration mutation, Modbus DataMap mutation,
archive query, archive maintenance/export, license installation and user management.
Denied direct calls must not invoke the underlying dispatcher, store or mutating service.

## Role Policy

`UserRole.User` is limited to Route Map viewing and equipment commands. Administrator
has administrative permissions, but license-gated commercial features still require a
valid current license state containing the requested feature.

## Recovery

User management and license installation are recovery surfaces. An Administrator can
open the License tab without a valid commercial license, export an installation request
and install a license. This does not grant access to Archive, RouteMap, diagnostics or
engineering features unless the license contains those feature names.

## Operational Notes

Disabling a user outside the running process by manually editing the database is not the
same as calling `SetUserEnabledAsync`; it is an operational risk documented for recovery
only. Normal operation must use the application service so the active session is revoked.
