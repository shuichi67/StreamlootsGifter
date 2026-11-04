using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace StreamlootsManager;

internal sealed record StoredTwitchTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>Stores Twitch tokens encrypted with Windows DPAPI for the current Windows user.</summary>
internal static class TwitchTokenStore
{
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamlootsGifter", "twitch-session.dat");
    private static readonly byte[] Entropy = "StreamlootsGifter.TwitchSession.v1"u8.ToArray();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static StoredTwitchTokens? Load()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var encrypted = File.ReadAllBytes(TokenPath);
            var clear = Unprotect(encrypted);
            try { return JsonSerializer.Deserialize<StoredTwitchTokens>(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
        {
            return null;
        }
    }

    public static void Save(StoredTwitchTokens tokens)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(tokens);
        byte[] encrypted;
        try { encrypted = Protect(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }

        var directory = Path.GetDirectoryName(TokenPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = TokenPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, TokenPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static byte[] Protect(byte[] clear) => Transform(clear, protect: true);
    private static byte[] Unprotect(byte[] encrypted) => Transform(encrypted, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = Allocate(input);
        var entropyBlob = Allocate(Entropy);
        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, "Streamloots Gifter Twitch session", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 1, out var outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob);
            if (!succeeded) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var output = new byte[outputBlob.Length];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally { if (outputBlob.Data != IntPtr.Zero) LocalFree(outputBlob.Data); }
        }
        finally
        {
            Free(inputBlob);
            Free(entropyBlob);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = data };
    }

    private static void Free(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        for (var index = 0; index < blob.Length; index++) Marshal.WriteByte(blob.Data, index, 0);
        Marshal.FreeHGlobal(blob.Data);
    }
}
