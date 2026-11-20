using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Reprise.Platform.Windows.Media;

/// <summary>Checks executable signatures as a local signal; this is not remote client attestation.</summary>
internal static class WindowsPlayerTrust
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, DateTime Checked, bool Trusted)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Requires a valid Authenticode signature from the expected player publisher.</summary>
    internal static bool IsTrusted(string path, string appId)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            var key = appId + "|" + path;
            if (Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp && DateTime.UtcNow - cached.Checked < TimeSpan.FromMinutes(5)) return cached.Trusted;
            var trusted = Verify(path, appId);
            Cache[key] = (stamp, DateTime.UtcNow, trusted);
            return trusted;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        { return false; }
    }

    /// <summary>Asks Windows to verify the signed file and releases the native trust state.</summary>
    private static bool Verify(string path, string appId)
    {
        var info = new FileInfoData { Size = (uint)Marshal.SizeOf<FileInfoData>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoData>());
        Marshal.StructureToPtr(info, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = pointer, StateAction = 1, ProviderFlags = 0x1000 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            var provider = WTHelperProvDataFromStateData(data.State);
            if (provider == IntPtr.Zero) return false;
            var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == IntPtr.Zero) return false;
            var certificate = WTHelperGetProvCertFromChain(signer, 0);
            if (certificate == IntPtr.Zero) return false;
            var context = Marshal.ReadIntPtr(certificate, IntPtr.Size == 8 ? 8 : 4);
            var name = new StringBuilder(512);
            if (CertGetNameString(context, 4, 0, IntPtr.Zero, name, name.Capacity) <= 1) return false;
            var publisher = name.ToString().Trim().TrimEnd('.');
            return appId.Equals("com.kakaoent.melon.player", StringComparison.OrdinalIgnoreCase)
                ? publisher.Equals("Kakao Entertainment Corp", StringComparison.OrdinalIgnoreCase)
                : publisher.Equals("Spotify AB", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            data.StateAction = 2;
            _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoData>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>Native WINTRUST_FILE_INFO layout.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoData { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File; public IntPtr Subject; }
    /// <summary>Native WINTRUST_DATA layout including optional signature settings.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr Policy; public IntPtr Sip; public uint UiChoice; public uint Revocation;
        public uint UnionChoice; public IntPtr File; public uint StateAction; public IntPtr State;
        public IntPtr Url; public uint ProviderFlags; public uint UiContext; public IntPtr SignatureSettings;
    }
    /// <summary>Validates a file under the system Authenticode policy.</summary>
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    /// <summary>Returns provider data for the verified Authenticode operation.</summary>
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    /// <summary>Returns the primary verified signer rather than an unverified file claim.</summary>
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    /// <summary>Returns a certificate in the verified signer chain.</summary>
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
    /// <summary>Reads the publisher's display name from a Windows certificate context.</summary>
    [DllImport("crypt32.dll", EntryPoint = "CertGetNameStringW", CharSet = CharSet.Unicode)] private static extern uint CertGetNameString(IntPtr certificate, uint type, uint flags, IntPtr typeData, StringBuilder name, int capacity);
}
