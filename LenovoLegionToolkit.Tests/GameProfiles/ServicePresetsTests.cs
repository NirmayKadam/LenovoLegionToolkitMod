using System;
using System.Linq;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameProfiles;

/// <summary>The service presets: nothing protected in them, no repeats, and every one explained in the app.</summary>
public class ServicePresetsTests
{
    [Fact]
    public void NoPreset_ListsAProtectedService()
    {
        var protectedOnes = ServicePresets.All.SelectMany(p => p.Services).Where(s => ServiceRules.Check(s, null) == ServiceRefusal.Protected);
        Assert.Empty(protectedOnes);
    }

    [Fact]
    public void NoServiceIsInTwoPresets_AndIdsAreUnique()
    {
        var services = ServicePresets.All.SelectMany(p => p.Services).ToArray();
        Assert.Equal(services.Length, services.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ServicePresets.All.Count, ServicePresets.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void EveryPreset_HasATitleAndANote_AndPresetsWithAWarningAreOffByDefault()
    {
        Assert.False(string.IsNullOrWhiteSpace(Resource.ServicePreset_Effect));
        Assert.All(ServicePresets.All, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Title")));
            Assert.False(string.IsNullOrWhiteSpace(Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Note")));
            if (preset.HasWarning)
                Assert.False(preset.OnByDefault);
        });
    }
}
