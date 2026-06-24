using Configurator.Application.Services.Licensing;
using System.Text.Json;

namespace Configurator.LicenseIssuer;

public sealed class LicenseIssuerCli
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly LicenseIssuerService _service;

    public LicenseIssuerCli(
        TextReader input,
        TextWriter output,
        TextWriter error,
        LicenseIssuerService? service = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _service = service ?? new LicenseIssuerService();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();

        if (args.Length == 0 || IsHelp(args[0]))
        {
            await PrintUsageAsync().ConfigureAwait(false);
            return LicenseIssuerExitCodes.Success;
        }

        try
        {
            var command = args[0];
            var options = ParseOptions(args.Skip(1).ToArray());
            return command switch
            {
                "generate-key" => await GenerateKeyAsync(options, cancellationToken).ConfigureAwait(false),
                "issue" => await IssueAsync(options, cancellationToken).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellationToken).ConfigureAwait(false),
                "inspect" => await InspectAsync(options, cancellationToken).ConfigureAwait(false),
                _ => await UsageErrorAsync($"Unknown command '{command}'.").ConfigureAwait(false)
            };
        }
        catch (ArgumentException ex)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return LicenseIssuerExitCodes.UsageError;
        }
        catch (InvalidDataException ex)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return LicenseIssuerExitCodes.ParseError;
        }
        catch (JsonException ex)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return LicenseIssuerExitCodes.ParseError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return LicenseIssuerExitCodes.IoError;
        }
    }

    private async Task<int> GenerateKeyAsync(
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken)
    {
        await _service.GenerateKeyAsync(
            Required(options, "key-id"),
            Required(options, "public-key"),
            Required(options, "private-key"),
            options.ContainsKey("test-key"),
            cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync("Key pair generated.").ConfigureAwait(false);

        return LicenseIssuerExitCodes.Success;
    }

    private async Task<int> IssueAsync(
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken)
    {
        var envelope = await _service.IssueAsync(
            Required(options, "profile"),
            Required(options, "private-key"),
            Required(options, "key-id"),
            Required(options, "out"),
            cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync($"License issued: {envelope.KeyId}").ConfigureAwait(false);

        return LicenseIssuerExitCodes.Success;
    }

    private async Task<int> VerifyAsync(
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken)
    {
        var result = await _service.VerifyAsync(
            Required(options, "license"),
            Required(options, "public-key"),
            options.ContainsKey("allow-test-key"),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var details = string.Join(
                "; ",
                result.Errors.Select(error => $"{error.Code}: {error.Message}"));
            await _error.WriteLineAsync($"License verification failed: {details}").ConfigureAwait(false);

            return LicenseIssuerExitCodes.VerificationFailed;
        }

        await _output.WriteLineAsync("License verification succeeded.").ConfigureAwait(false);

        return LicenseIssuerExitCodes.Success;
    }

    private async Task<int> InspectAsync(
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken)
    {
        var payloadJson = await _service
            .InspectAsync(Required(options, "license"), cancellationToken)
            .ConfigureAwait(false);
        await _output.WriteLineAsync(payloadJson).ConfigureAwait(false);

        return LicenseIssuerExitCodes.Success;
    }

    private async Task<int> UsageErrorAsync(string message)
    {
        await _error.WriteLineAsync(message).ConfigureAwait(false);
        await PrintUsageAsync().ConfigureAwait(false);

        return LicenseIssuerExitCodes.UsageError;
    }

    private Task PrintUsageAsync()
        => _output.WriteLineAsync(
            """
            promflow-license generate-key --key-id <id> --public-key <path> --private-key <path> [--test-key]
            promflow-license issue --profile <path> --private-key <path> --key-id <id> --out <path>
            promflow-license verify --license <path> --public-key <path> [--allow-test-key]
            promflow-license inspect --license <path>
            """);

    private static Dictionary<string, string?> ParseOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                throw new ArgumentException($"Unexpected argument '{token}'.");
            }

            var name = token[2..];
            if (options.ContainsKey(name))
            {
                throw new ArgumentException($"Duplicate option '--{name}'.");
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = null;
                continue;
            }

            options[name] = args[++index];
        }

        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '--{name}'.");
        }

        return value;
    }

    private static bool IsHelp(string value)
        => string.Equals(value, "--help", StringComparison.Ordinal)
            || string.Equals(value, "-h", StringComparison.Ordinal)
            || string.Equals(value, "help", StringComparison.Ordinal);
}
