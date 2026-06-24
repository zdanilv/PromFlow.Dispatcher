namespace Configurator.Application.Services.Authorization;

public sealed class UserSessionSnapshot
{
    private UserSessionSnapshot(UserSession? session)
    {
        Session = session;
    }

    public static UserSessionSnapshot Anonymous { get; } = new(null);

    public bool IsAuthenticated => Session is not null;

    public UserSession? Session { get; }

    public static UserSessionSnapshot Authenticated(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new UserSessionSnapshot(session);
    }
}
