using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// Uses real files: dotnet.exe carries an embedded Microsoft Authenticode signature.
public class AuthenticodeVerifierTests : IDisposable
{
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

    private static readonly string SignedFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");

    public AuthenticodeVerifierTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, true);

    private string CopyWithFlippedByte()
    {
        var bytes = File.ReadAllBytes(SignedFile);
        // A byte in the middle of the code, well away from the PE headers and the signature blob at the end.
        bytes[bytes.Length / 3] ^= 0xFF;
        var path = Path.Combine(_tempDir, "tampered.exe");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ValidSignedFile_Passes()
    {
        Assert.True(File.Exists(SignedFile), $"Test needs {SignedFile}");
        Assert.Equal(0, AuthenticodeVerifier.Verify(SignedFile));
    }

    [Fact]
    public void TamperedFile_FailsWithBadDigest()
    {
        Assert.Equal(TRUST_E_BAD_DIGEST, AuthenticodeVerifier.Verify(CopyWithFlippedByte()));
    }

    [Fact]
    public void TamperedFile_StillYieldsCertificate_FromCreateFromSignedFile()
    {
        // Documents why the old check was not enough: the certificate (and so its thumbprint)
        // can still be read from a file whose contents no longer match the signature.
        var tampered = CopyWithFlippedByte();
#pragma warning disable SYSLIB0057
        using var original = new X509Certificate2(X509Certificate.CreateFromSignedFile(SignedFile));
        using var fromTampered = new X509Certificate2(X509Certificate.CreateFromSignedFile(tampered));
#pragma warning restore SYSLIB0057

        Assert.Equal(original.Thumbprint, fromTampered.Thumbprint);
        Assert.NotEqual(0, AuthenticodeVerifier.Verify(tampered));
    }

    [Fact]
    public void UnsignedFile_Fails()
    {
        var path = Path.Combine(_tempDir, "unsigned.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);

        Assert.NotEqual(0, AuthenticodeVerifier.Verify(path));
    }
}
