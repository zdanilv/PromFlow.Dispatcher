namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Defines whether command delivery should continue when the initial command audit cannot be queued.
/// </summary>
public enum CommandAuditFailureMode
{
    FailOpen,
    FailClosed
}
