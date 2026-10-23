using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GhostDeck;

/// <summary>
/// Whether a file carries a valid Authenticode signature from THIS project's release identity.
/// The in-app updater runs this on the downloaded release asset before anything is swapped:
/// the asset comes from a GitHub release, and a release asset can be replaced by anyone with
/// write access to the repository without going through the signing step in release.yml, so
/// the build-time signature gate alone proves nothing about the file that is served later.
///
/// What "ours" means. Releases are signed by Azure Trusted Signing, which issues a fresh
/// certificate every few days (a measured one lived from 21 to 24 October 2026), so a pinned
/// thumbprint would stop matching within a week. Two things are stable across that rotation:
/// the durable identity EKU that Trusted Signing puts into every certificate issued for one
/// identity validation (<see cref="IdentityEku"/>), and the publisher name in the subject, the
/// same string release.yml checks. Both have to match. The chain, the timestamp and the
/// signature itself are judged by <c>WinVerifyTrust</c>, exactly as the UAC prompt and
/// SmartScreen would judge the file if the user had downloaded it by hand - which is the whole
/// point: the in-app path shows neither of those, so it has to do their check itself.
/// </summary>
public static class Authenticode
{
    /// <summary>The Trusted Signing durable identity of the GhostDeck releases.</summary>
    public const string IdentityEku = "1.3.6.1.4.1.311.97.227492317.585399833.189491622.408323361";
    /// <summary>Part of the subject's common name; release.yml refuses to publish without it.</summary>
    public const string PublisherName = "FENIX INSPIRE";

    /// <summary>
    /// True when <paramref name="path"/> is signed, the signature verifies (chain to a trusted
    /// root, timestamp honoured, no user interface) and the signer is the release identity.
    /// <paramref name="why"/> says in English what failed, for the log and the page.
    /// </summary>
    public static bool IsOurs(string path, out string why)
    {
        why = "";
        int rc = VerifyTrust(path);
        if (rc != 0)
        {
            why = rc switch
            {
                TRUST_E_NOSIGNATURE => "the file is not signed",
                TRUST_E_BAD_DIGEST => "the file was changed after it was signed",
                CERT_E_UNTRUSTEDROOT => "the signing certificate does not chain to a trusted root",
                TRUST_E_EXPLICIT_DISTRUST => "the signing certificate is explicitly distrusted",
                _ => $"the signature did not verify (0x{rc:X8})",
            };
            return false;
        }

        X509Certificate2 cert;
        try
        {
            using var raw = X509Certificate.CreateFromSignedFile(path);
            cert = new X509Certificate2(raw);
        }
        catch (Exception ex)
        {
            why = "the signer certificate could not be read: " + ex.Message;
            return false;
        }
        using (cert)
        {
            string name = cert.GetNameInfo(X509NameType.SimpleName, false);
            bool identity = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == IdentityEku));
            if (!identity) { why = $"signed by another identity ({name})"; return false; }
            if (!name.Contains(PublisherName, StringComparison.OrdinalIgnoreCase))
            {
                why = $"unexpected publisher name ({name})";
                return false;
            }
        }
        return true;
    }

    // ---------------- WinVerifyTrust ----------------

    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);

    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    // No network: an offline machine must still be able to judge a file whose chain and
    // timestamp are complete in the signature itself. Revocation of a three-day certificate
    // is not worth a failed update behind a captive portal.
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
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

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, in Guid pgActionID, ref WintrustData pWVTData);

    private static int VerifyTrust(string path)
    {
        IntPtr pathPtr = IntPtr.Zero, filePtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUni(path);
            var fi = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(), pcwszFilePath = pathPtr };
            filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(fi, filePtr, false);
            var wd = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = filePtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var hwnd = new IntPtr(-1);   // INVALID_HANDLE_VALUE: never show a dialog
            int rc;
            try { rc = WinVerifyTrust(hwnd, in WintrustActionGenericVerifyV2, ref wd); }
            finally
            {
                wd.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(hwnd, in WintrustActionGenericVerifyV2, ref wd);
            }
            return rc;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return unchecked((int)0x80004005);   // E_FAIL: no verifier at all = not verified
        }
        finally
        {
            if (filePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(filePtr);
            if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
