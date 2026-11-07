using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Station.Core;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Builds real folder layouts in a temp folder.
public class PluginDiscoveryTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");

    public PluginDiscoveryTests() => Directory.CreateDirectory(_appData);

    public void Dispose() => Directory.Delete(_appData, true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_appData, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return path;
    }

    [Fact]
    public void NoOldFolders_FindsNothing()
    {
        Assert.Empty(PluginDiscovery.FindLegacyPluginDlls(_appData));
    }

    [Fact]
    public void FindsAllLayouts_InBothOldFolderNames()
    {
        var expected = new[]
        {
            Touch("Plugins", "Top.dll"),
            Touch("Plugins", "Dependency", "Dep.dll"),
            Touch("Plugins", "Nested", "Nested.dll"),
            Touch("Plugin", "Singular.dll"),
        };

        var found = PluginDiscovery.FindLegacyPluginDlls(_appData);

        Assert.Equal(expected.OrderBy(p => p), found.OrderBy(p => p));
    }

    [Fact]
    public void ConfigsAndNonMatchingSubfolderDlls_AreIgnored()
    {
        Touch("Plugins", "Configs", "MyPlugin", "plugin.json");
        Touch("Plugins", "Configs", "Configs.json");
        Touch("Plugins", "Nested", "Other.dll"); // not <folder>\<folder>.dll

        Assert.Empty(PluginDiscovery.FindLegacyPluginDlls(_appData));
    }
}
