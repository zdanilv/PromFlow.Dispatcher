using Configurator.Application.Services.Archiving;
using System.Globalization;
using System.Text;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveCsvWriter
{
    public Task WriteHeaderAsync(
        StreamWriter writer,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
        => WriteRowAsync(writer, columns, cancellationToken);

    public Task WriteCommandAsync(
        StreamWriter writer,
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken)
    {
        var commandOutcome = record switch { { Result: var value } => value };
        return WriteRowAsync(
            writer,
            [
                record.CommandId.ToString("D"),
                record.CorrelationId.ToString("D"),
                FormatUtc(record.RequestedAtUtc),
                FormatUtc(record.CompletedAtUtc),
                record.SessionId,
                record.UserId,
                record.Username,
                record.DeviceId,
                record.SignalId,
                record.ValueType.ToString(),
                record.RequestedValueCanonical,
                record.WriteMode?.ToString(),
                commandOutcome.ToString(),
                record.ErrorCode,
                record.ErrorMessage,
                record.ConfirmationStatus.ToString(),
                FormatUtc(record.ConfirmedAtUtc),
                record.SchemaVersion.ToString(CultureInfo.InvariantCulture)
            ],
            cancellationToken);
    }

    public Task WritePhysicalWriteAsync(
        StreamWriter writer,
        PhysicalModbusWriteAuditRecord record,
        CancellationToken cancellationToken)
        => WriteRowAsync(
            writer,
            [
                record.WriteId.ToString("D"),
                record.CommandId?.ToString("D"),
                FormatUtc(record.AttemptedAtUtc),
                FormatUtc(record.CompletedAtUtc),
                record.Role.ToString(),
                record.Area.ToString(),
                record.Address.ToString(CultureInfo.InvariantCulture),
                record.Quantity.ToString(CultureInfo.InvariantCulture),
                ToHex(record.PayloadBlob),
                record.Succeeded ? "true" : "false",
                record.ErrorCode,
                record.ErrorMessage,
                record.SchemaVersion.ToString(CultureInfo.InvariantCulture)
            ],
            cancellationToken);

    public Task WriteRuntimeEventAsync(
        StreamWriter writer,
        ArchiveRuntimeEventRecord record,
        CancellationToken cancellationToken)
        => WriteRowAsync(
            writer,
            [
                record.Id.ToString("D"),
                FormatUtc(record.OccurredAtUtc),
                record.DeviceId,
                record.EventType,
                record.Severity.ToString(CultureInfo.InvariantCulture),
                record.Message,
                record.DetailsJson
            ],
            cancellationToken);

    public Task WriteRowAsync(
        StreamWriter writer,
        IReadOnlyList<string?> values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);

        var line = new StringBuilder();
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                line.Append(',');
            }

            AppendEscaped(line, values[index]);
        }

        return writer.WriteLineAsync(line.ToString().AsMemory(), cancellationToken);
    }

    private static void AppendEscaped(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var mustQuote = value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal);
        if (!mustQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        foreach (var character in value)
        {
            if (character == '"')
            {
                builder.Append("\"\"");
            }
            else
            {
                builder.Append(character);
            }
        }

        builder.Append('"');
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatUtc(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string ToHex(IReadOnlyList<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Count * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
