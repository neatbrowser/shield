using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NeatShield.Core.Security;

/// <summary>
/// Protects a small secret (the password of the root CA's key file) with Windows DPAPI, bound to the
/// current user. On other platforms (tests) it is only encoded, not protected.
/// </summary>
internal static class SecretStore
{
    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "plain:";
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (!OperatingSystem.IsWindows())
        {
            return PlainPrefix + Convert.ToBase64String(bytes);
        }

        return DpapiPrefix + Convert.ToBase64String(Dpapi(bytes, protect: true));
    }

    /// <summary>Returns null when the value is missing or cannot be read (another user, another machine, damaged).</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
            }

            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
            {
                var data = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
                return Encoding.UTF8.GetString(Dpapi(data, protect: false));
            }
        }
        catch (Exception)
        {
            // fall through
        }

        return null;
    }

    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static byte[] Dpapi(byte[] input, bool protect)
    {
        var pin = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var blobIn = new DataBlob { cbData = input.Length, pbData = pin.AddrOfPinnedObject() };
            DataBlob blobOut;
            var ok = protect
                ? CryptProtectData(ref blobIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out blobOut)
                : CryptUnprotectData(ref blobIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out blobOut);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[blobOut.cbData];
                Marshal.Copy(blobOut.pbData, result, 0, blobOut.cbData);
                return result;
            }
            finally
            {
                LocalFree(blobOut.pbData);
            }
        }
        finally
        {
            pin.Free();
        }
    }
}
