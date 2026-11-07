using System;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Real files: an unsigned file and a copy of the Microsoft-signed dotnet.exe (valid signature, wrong signer).
public class VerifiedDownloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void UnsignedInstaller_IsRejected_AndDeleted()
    {
        var path = Path.Combine(_dir, "setup.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);

        Assert.Throws<SecurityException>(() => UpdateChecker.LockAndVerify(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ValidlySignedInstaller_FromWrongSigner_IsRejected_AndDeleted()
    {
        var path = Path.Combine(_dir, "setup.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"), path);

        var ex = Assert.Throws<SecurityException>(() => UpdateChecker.LockAndVerify(path));
        Assert.Contains("Thumbprint", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void VerifiedDownload_KeepsFileLocked_UntilDisposed()
    {
        var path = Path.Combine(_dir, "setup.exe");
        File.WriteAllBytes(path, [1, 2, 3]);

        var download = new VerifiedDownload(path, LockedFile.Open(path));

        Assert.ThrowsAny<IOException>(() => File.OpenWrite(path).Dispose());
        Assert.ThrowsAny<Exception>(() => File.Delete(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));

        download.Dispose();

        File.WriteAllBytes(path, [9]);
        Assert.Equal([9], File.ReadAllBytes(path));
    }
}
