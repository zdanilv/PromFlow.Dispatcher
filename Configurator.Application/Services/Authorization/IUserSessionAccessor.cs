namespace Configurator.Application.Services.Authorization;

public interface IUserSessionAccessor
{
    UserSessionSnapshot Current { get; }

    void SetCurrent(UserSession session);

    void Clear();

    bool ClearIfCurrent(Guid userId);
}
