using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Configurator.Application.Services.Authorization;

namespace Configurator.Infrastructure.Security;

public sealed class WindowsCredentialProtector : ICredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PromFlow.Dispatcher.LoginCredentialStore.v1");

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureWindows();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectCore(plainBytes, protect: true);
        return Convert.ToBase64String(protectedBytes);
    }

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedValue) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var plainBytes = ProtectCore(protectedBytes, protect: false);
            plaintext = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static byte[] ProtectCore(byte[] input, bool protect)
    {
        var inputBlob = DataBlob.FromBytes(input);
        var entropyBlob = DataBlob.FromBytes(Entropy);
        DataBlob outputBlob = default;
        try
        {
            var ok = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob)
                : CryptUnprotectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
            if (!ok)
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            var output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            inputBlob.Free();
            entropyBlob.Free();
            outputBlob.FreeWithLocalFree();
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Saved login passwords require Windows DPAPI.");
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;

        public static DataBlob FromBytes(byte[] bytes)
        {
            var blob = new DataBlob
            {
                Size = bytes.Length,
                Data = Marshal.AllocHGlobal(bytes.Length)
            };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        public void Free()
        {
            if (Data == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
            Size = 0;
        }

        public void FreeWithLocalFree()
        {
            if (Data == IntPtr.Zero)
            {
                return;
            }

            LocalFree(Data);
            Data = IntPtr.Zero;
            Size = 0;
        }
    }
}
