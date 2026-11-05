using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SignMyApp.Core;

internal static class NativeSignature
{
    internal const int NoSignature = unchecked((int)0x800B0100);
    internal const int BadDigest = unchecked((int)0x80096010);

    internal static int Verify(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file, filePointer, false);
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = filePointer, StateAction = 1,
            // No UI, no revocation retrieval, no AIA/CRL network access.
            ProviderFlags = 0x10 | 0x1000
        };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    internal static byte[]? ReadMessage(string path)
    {
        // CryptQueryObject uses the PE or MSI SIP to extract the embedded PKCS#7.
        // CERT_QUERY_OBJECT_FILE, PKCS7_SIGNED[_EMBED], FORMAT_BINARY.
        var ok = CryptQueryObject(1, path, (1u << 8) | (1u << 10), 2, 0,
            out _, out _, out _, out var store, out var message, IntPtr.Zero);
        try
        {
            if (!ok || message == IntPtr.Zero) return null;
            uint size = 0;
            if (!CryptMsgGetParam(message, 29, 0, null, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (size > 64 * 1024 * 1024) throw new InvalidDataException("Signature data is too large.");
            var bytes = new byte[size];
            if (!CryptMsgGetParam(message, 29, 0, bytes, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return bytes;
        }
        finally
        {
            if (message != IntPtr.Zero) CryptMsgClose(message);
            if (store != IntPtr.Zero) CertCloseStore(store, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, UrlReference;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptQueryObject(uint objectType, string path, uint contentFlags,
        uint formatFlags, uint flags, out uint encoding, out uint content, out uint format,
        out IntPtr store, out IntPtr message, IntPtr context);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptMsgGetParam(IntPtr message, uint type, uint index,
        [Out] byte[]? data, ref uint size);
    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptMsgClose(IntPtr message);
    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertCloseStore(IntPtr store, uint flags);
}
