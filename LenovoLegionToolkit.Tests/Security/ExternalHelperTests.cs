using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.PackageDownloader.Detectors.Rules;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Uses real files and a real copy of the Microsoft-signed dotnet.exe.
public class ExternalHelperTests : IDisposable
{
    private static readonly string DotnetExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData("installsetting_2312.exe", true)]
    [InlineData("wwdf0tae40_82xv_11", true)]
    [InlineData(@"..\evil.exe", false)]
    [InlineData(@"sub\file.exe", false)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeFileName(string? name, bool expected)
    {
        Assert.Equal(expected, ExternalDetectionRule.IsSafeFileName(name));
    }

    [Fact]
    public void Command_BareName_ResolvesIntoWorkFolder()
    {
        var (exe, args) = ExternalDetectionRule.ResolveCommand("InstallSetting_2312.exe -yledver 2.1.1.16 -xtu 7.12.0.29", _dir);

        Assert.Equal(Path.Combine(_dir, "InstallSetting_2312.exe"), exe);
        Assert.Equal("-yledver 2.1.1.16 -xtu 7.12.0.29", args);
    }

    [Fact]
    public void Command_PackagePathVariable_IsExpanded()
    {
        var (exe, args) = ExternalDetectionRule.ResolveCommand(@"%PACKAGEPATH%\usbcundkg2fw1024_2_check.exe /1024 /x.exe", _dir);

        Assert.Equal(Path.Combine(_dir, "usbcundkg2fw1024_2_check.exe"), exe);
        Assert.Equal("/1024 /x.exe", args);
    }

    [Fact]
    public void Command_AbsolutePathElsewhere_IsNotInWorkFolder()
    {
        var (exe, _) = ExternalDetectionRule.ResolveCommand(@"C:\Windows\System32\cmd.exe /c whoami", _dir);

        Assert.NotEqual(Path.GetFullPath(Path.Combine(_dir, "cmd.exe")), Path.GetFullPath(exe!));
    }

    [Fact]
    public async Task LockedFile_CannotBeChangedOrDeleted_ButCanStillRun()
    {
        // hostname.exe is a standalone Windows program that still works after being copied elsewhere.
        var copy = Path.Combine(_dir, "helper.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "hostname.exe"), copy);

        using (LockedFile.Open(copy))
        {
            Assert.ThrowsAny<IOException>(() => File.OpenWrite(copy).Dispose());
            Assert.ThrowsAny<Exception>(() => File.Delete(copy));
            Assert.ThrowsAny<Exception>(() => File.Move(copy, Path.Combine(_dir, "moved.exe")));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var (exitCode, output) = await CMD.RunAsync(copy, string.Empty, token: cts.Token);

            Assert.Equal(0, exitCode);
            Assert.Equal(Environment.MachineName, output.Trim(), ignoreCase: true);
        }

        // Once released, it can be removed again.
        File.Delete(copy);
    }

    [Fact]
    public void SignerOrganization_OfMicrosoftFile_IsNotLenovo()
    {
        Assert.Equal(0, AuthenticodeVerifier.Verify(DotnetExe));
        Assert.Equal("Microsoft Corporation", AuthenticodeVerifier.GetSignerOrganization(DotnetExe));
    }

    [Fact]
    public void SignerOrganization_OfUnsignedFile_IsNull()
    {
        var unsigned = Path.Combine(_dir, "unsigned.exe");
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 0, 0]);

        Assert.NotEqual(0, AuthenticodeVerifier.Verify(unsigned));
        Assert.Null(AuthenticodeVerifier.GetSignerOrganization(unsigned));
    }
}
