using System.Text;

namespace Configurator.Application.Services.Licensing;

public static class Base64UrlCodec
{
    public static string Encode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('=', StringComparison.Ordinal)
            || value.Any(character => character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                and not '_'))
        {
            return false;
        }

        var padding = value.Length % 4;
        if (padding == 1)
        {
            return false;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        if (padding > 0)
        {
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');
        }

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    public static string EncodeUtf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Encode(Encoding.UTF8.GetBytes(text));
    }
}
