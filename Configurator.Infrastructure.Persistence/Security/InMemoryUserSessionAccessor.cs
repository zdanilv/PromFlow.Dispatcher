using Configurator.Application.Services.Authorization;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class InMemoryUserSessionAccessor : IUserSessionAccessor
{
    private readonly object _gate = new();
    private UserSessionSnapshot _current = UserSessionSnapshot.Anonymous;

    public UserSessionSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void SetCurrent(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_gate)
        {
            _current = UserSessionSnapshot.Authenticated(session);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = UserSessionSnapshot.Anonymous;
        }
    }

    public bool ClearIfCurrent(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        }

        lock (_gate)
        {
            if (_current.Session?.UserId != userId)
            {
                return false;
            }

            _current = UserSessionSnapshot.Anonymous;
            return true;
        }
    }
}
