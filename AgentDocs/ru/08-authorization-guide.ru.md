# Руководство по авторизации

В PromFlow авторизация использует два независимых входа: роль authenticated user и
offline product license. Administrator permissions не обходят commercial license
features. Видимость UI является удобством, но не границей безопасности; граница должна
быть в сервисах.

## Login и session lifecycle

При старте приложение открывает administrator bootstrap, если пользователей нет, иначе
открывает login. Workspace создается только после успешной authentication. Logout
вызывает `IAuthenticationService`, очищает текущую session и dispose workspace content.

Stage 14 закрывает gap с disabled active session. Метод
`IUserSessionAccessor.ClearIfCurrent(Guid)` очищает текущую in-process session, когда
`UserManagementService.SetUserEnabledAsync` успешно отключает этого же пользователя.
Audit остается best effort и не должен оставлять disabled user authenticated.

## Permission boundaries

Для service-boundary проверок используется `IAccessDecisionService`. Защищенные
поверхности: equipment commands, RouteMap configuration mutation, Modbus DataMap
mutation, archive query, archive maintenance/export, license installation и user
management. Denied direct calls не должны вызывать inner dispatcher, store или mutation
service.

## Роли

`UserRole.User` ограничен просмотром Route Map и equipment commands. Administrator
имеет administrative permissions, но license-gated commercial features требуют валидную
current license state с нужным feature.

## Recovery

User management и license installation являются recovery surfaces. Administrator может
открыть License tab без valid commercial license, экспортировать installation request и
установить license. Это не дает доступ к Archive, RouteMap, diagnostics или engineering
features без соответствующих features в license.

## Операционные заметки

Отключение пользователя внешней правкой базы вне процесса приложения не равно вызову
`SetUserEnabledAsync`; это documented recovery risk. В нормальной эксплуатации нужно
использовать application service, чтобы active session была отозвана.
