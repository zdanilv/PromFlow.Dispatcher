using System.Globalization;
using System.Security.Cryptography;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveChecksum
{
    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return ToHex(hash);
    }

    public string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return ToHex(hash);
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            chars[index * 2] = GetHex(value >> 4);
            chars[(index * 2) + 1] = GetHex(value & 0x0F);
        }

        return new string(chars);
    }

    private static char GetHex(int value)
        => value.ToString("x1", CultureInfo.InvariantCulture)[0];
}
