using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Checks a file's embedded Authenticode signature with WinVerifyTrust: the signature must be valid
/// for the file's current contents and chain to a trusted root.
/// (X509Certificate.CreateFromSignedFile only extracts the certificate and validates nothing.)
/// </summary>
internal static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

    /// <summary>
    /// The organization (O=) of the certificate that signed the file, or null if it can't be read.
    /// Only meaningful after <see cref="Verify"/> returned 0.
    /// </summary>
    public static string? GetSignerOrganization(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057

            foreach (var rdn in cert.SubjectName.EnumerateRelativeDistinguishedNames())
            {
                if (rdn.GetSingleElementType().Value == "2.5.4.10") // O = Organization
                    return rdn.GetSingleElementValue();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns 0 when the signature is valid and trusted, otherwise the WinVerifyTrust HRESULT
    /// (for example 0x80096010 TRUST_E_BAD_DIGEST for a modified file, 0x800B0100 TRUST_E_NOSIGNATURE).
    /// </summary>
    public static int Verify(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE,
            };

            var result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);

            // Always release the state WinVerifyTrust allocated.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);

            return result;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }
}
