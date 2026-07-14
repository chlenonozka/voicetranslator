using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceTranslator.App.Services;

internal static class CurrentUserDataProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Transform(plaintext, protect: true);
    }

    public static byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return Transform(ciphertext, protect: false);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        GCHandle inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var inputBlob = new DataBlob
            {
                Size = input.Length,
                Data = inputHandle.AddrOfPinnedObject(),
            };
            bool succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    "Voice Translator voice profile",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out DataBlob outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                _ = LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            inputHandle.Free();
        }
    }

    [DllImport(
        "crypt32.dll",
        EntryPoint = "CryptProtectData",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        int flags,
        out DataBlob dataOut);

    [DllImport(
        "crypt32.dll",
        EntryPoint = "CryptUnprotectData",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }
}
