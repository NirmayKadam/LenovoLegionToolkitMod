using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// The updater's rules: which repository, which releases count, which page is opened. No network.
public class UpdateCheckerTests
{
    [Fact]
    public void ChecksThisForkOnly()
    {
        Assert.Equal("NirmayKadam", UpdateChecker.RepositoryOwner);
        Assert.Equal("LenovoLegionToolkitMod", UpdateChecker.RepositoryName);
        Assert.Equal("https://github.com/NirmayKadam/LenovoLegionToolkitMod/releases/latest", UpdateChecker.LatestReleasePage.AbsoluteUri);
    }

    [Theory]
    [InlineData("v2.36.10.0", "2.36.10.0")]
    [InlineData("2.36.10", "2.36.10")]
    [InlineData("2.36.10-beta", "2.36.10")]
    [InlineData("V3.0", "3.0")]
    public void ReleaseTags_GiveVersions(string tag, string version) =>
        Assert.Equal(Version.Parse(version), UpdateChecker.TryParseReleaseVersion(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("release")]
    public void TagsWithoutAVersion_AreIgnored(string? tag) =>
        Assert.Null(UpdateChecker.TryParseReleaseVersion(tag));

    [Theory]
    [InlineData("main", false, UpdateChannel.Stable, true)]
    [InlineData("master", false, UpdateChannel.Stable, true)]
    [InlineData("refs/heads/main", false, UpdateChannel.Stable, true)]
    [InlineData(null, false, UpdateChannel.Stable, true)]
    [InlineData("main", true, UpdateChannel.Stable, false)]
    [InlineData("main", true, UpdateChannel.Beta, true)]
    [InlineData("dev", false, UpdateChannel.Stable, false)]
    [InlineData("dev", false, UpdateChannel.Dev, true)]
    [InlineData(null, false, UpdateChannel.Dev, false)]
    [InlineData("test", false, UpdateChannel.Test, true)]
    public void Channels_FollowTheBranch(string? branch, bool prerelease, UpdateChannel channel, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsMatchingChannel(branch, prerelease, channel));

    [Fact]
    public void AReleasePageOfThisFork_IsOpened() =>
        Assert.Equal("https://github.com/NirmayKadam/LenovoLegionToolkitMod/releases/tag/v2.36.10.0",
            UpdateChecker.ReleasePageOrDefault("https://github.com/NirmayKadam/LenovoLegionToolkitMod/releases/tag/v2.36.10.0").AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://github.com/NirmayKadam/LenovoLegionToolkitMod/releases/tag/v1")]
    [InlineData("https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/releases/tag/v3")]
    [InlineData("https://example.com/NirmayKadam/LenovoLegionToolkitMod/releases/tag/v1")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public void AnyOtherAddress_FallsBackToTheLatestReleasePage(string? page) =>
        Assert.Equal(UpdateChecker.LatestReleasePage, UpdateChecker.ReleasePageOrDefault(page));

    [Fact]
    public void OldSettingsWithTheRemovedServerMethod_StillLoad()
    {
        const string json = """{ "LastUpdateCheckDateTime": null, "UpdateCheckFrequency": "PerWeek", "UpdateChannel": "Beta", "UpdateMethod": "Server" }""";

        var store = JsonConvert.DeserializeObject<UpdateSettings.UpdateSettingsStore>(json, AbstractSettings<UpdateSettings.UpdateSettingsStore>.CreateJsonSerializerSettings());

        Assert.NotNull(store);
        Assert.Equal(UpdateCheckFrequency.PerWeek, store.UpdateCheckFrequency);
        Assert.Equal(UpdateChannel.Beta, store.UpdateChannel);
    }
}
