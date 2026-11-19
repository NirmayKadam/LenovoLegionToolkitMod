using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using NeoSmart.AsyncLock;
using Octokit;
using Octokit.Internal;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Checks this fork's GitHub releases for a newer version. It only reports one and opens its release page:
/// nothing is downloaded or installed. (Upstream's updater downloaded and silently installed upstream's builds,
/// which would have replaced this fork, and also had a second, plain-HTTP update server.)
/// </summary>
public class UpdateChecker
{
    internal const string RepositoryOwner = "NirmayKadam";
    internal const string RepositoryName = "LenovoLegionToolkitMod";

    /// <summary>The fork's latest release page; opened when a release has no page of its own.</summary>
    public static readonly Uri LatestReleasePage = new($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private readonly HttpClientFactory _httpClientFactory;
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
    private readonly AsyncLock _updateSemaphore = new();

    private DateTime _lastUpdate;
    private TimeSpan _minimumTimeSpanForRefresh;
    private Update[] _updates = [];

    public bool Disable { get; set; }
    public UpdateCheckStatus Status { get; set; }

    public UpdateChecker(HttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        UpdateMinimumTimeSpanForRefresh();
        _lastUpdate = _updateSettings.Store.LastUpdateCheckDateTime ?? DateTime.MinValue;
    }

    public async Task<Version?> CheckAsync(bool forceCheck)
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
        {
            if (Disable)
            {
                _lastUpdate = DateTime.UtcNow;
                _updates = [];
                return null;
            }

            var shouldCheck = DateTime.UtcNow - _lastUpdate > _minimumTimeSpanForRefresh;

            try
            {
                if (!forceCheck && !shouldCheck)
                    return _updates.Length != 0 ? _updates.First().Version : null;

                Log.Instance.Trace($"Checking GitHub for updates... [repository={RepositoryOwner}/{RepositoryName}]");

                _updates = [];

                var adapter = new HttpClientAdapter(_httpClientFactory.CreateHandler);
                var connection = new Connection(new ProductHeaderValue("LenovoLegionToolkit-UpdateChecker"), adapter);
                var githubClient = new GitHubClient(connection);
                var releases = await githubClient.Repository.Release.GetAll(RepositoryOwner, RepositoryName, new ApiOptions { PageSize = 5 }).ConfigureAwait(false);

                var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
                var thisBuildDate = Assembly.GetEntryAssembly()?.GetBuildDateTime() ?? new DateTime(2000, 1, 1);
                var updateChannel = _updateSettings.Store.UpdateChannel;

                Log.Instance.Trace($"Found {releases.Count} releases. Current: {thisReleaseVersion} built on {thisBuildDate:yyyy-MM-dd}. Channel: {updateChannel}");

                _updates = releases
                    .Where(r => !r.Draft)
                    .Where(r => IsMatchingChannel(r.TargetCommitish, r.Prerelease, updateChannel))
                    .Where(r => (r.PublishedAt ?? r.CreatedAt).UtcDateTime >= thisBuildDate)
                    .Select(r => new { Release = r, Version = TryParseReleaseVersion(r.TagName) })
                    .Where(r => r.Version is not null && r.Version > thisReleaseVersion)
                    .OrderByDescending(r => r.Version)
                    .Select(r => new Update(r.Release))
                    .ToArray();

                Log.Instance.Trace($"Checked [updates.Length={_updates.Length}]");

                Status = UpdateCheckStatus.Success;
                return _updates.Length != 0 ? _updates.First().Version : null;
            }
            catch (RateLimitExceededException ex)
            {
                Log.Instance.Trace($"Reached API Rate Limitation.", ex);

                Status = UpdateCheckStatus.RateLimitReached;
                return null;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error checking for updates via GitHub.", ex);

                Status = UpdateCheckStatus.Error;
                return null;
            }
            finally
            {
                _lastUpdate = DateTime.UtcNow;
                _updateSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                _updateSettings.SynchronizeStore();
            }
        }
    }

    public async Task<Update[]> GetUpdatesAsync()
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
            return _updates;
    }

    public void UpdateMinimumTimeSpanForRefresh() => _minimumTimeSpanForRefresh = _updateSettings.Store.UpdateCheckFrequency switch
    {
        UpdateCheckFrequency.Never => TimeSpan.FromSeconds(0),
        UpdateCheckFrequency.PerHour => TimeSpan.FromHours(1),
        UpdateCheckFrequency.PerThreeHours => TimeSpan.FromHours(3),
        UpdateCheckFrequency.PerTwelveHours => TimeSpan.FromHours(12),
        UpdateCheckFrequency.PerDay => TimeSpan.FromDays(1),
        UpdateCheckFrequency.PerWeek => TimeSpan.FromDays(7),
        UpdateCheckFrequency.PerMonth => TimeSpan.FromDays(30),
        _ => throw new ArgumentException(nameof(_updateSettings.Store.UpdateCheckFrequency))
    };

    /// <summary>
    /// The page to open for a release. The address comes from GitHub's answer, so only a page of this fork's
    /// releases is opened; anything else falls back to <see cref="LatestReleasePage"/>.
    /// </summary>
    public static Uri ReleasePageOrDefault(string? releasePage)
    {
        var prefix = $"/{RepositoryOwner}/{RepositoryName}/releases/";

        if (Uri.TryCreate(releasePage, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return uri;

        return LatestReleasePage;
    }

    /// <summary>
    /// Stable and Beta releases are made from the default branch (main; master is accepted too), Dev and Test from
    /// branches of those names. A release with no branch is treated as made from the default branch.
    /// </summary>
    internal static bool IsMatchingChannel(string? targetCommitish, bool prerelease, UpdateChannel updateChannel) => updateChannel switch
    {
        UpdateChannel.Stable => IsOnDefaultBranch(targetCommitish) && !prerelease,
        UpdateChannel.Beta => IsOnDefaultBranch(targetCommitish) && prerelease,
        UpdateChannel.Dev => IsOnBranch(targetCommitish, "dev") && !prerelease,
        UpdateChannel.Test => IsOnBranch(targetCommitish, "test") && !prerelease,
        _ => false
    };

    private static bool IsOnDefaultBranch(string? target) =>
        string.IsNullOrWhiteSpace(target) || IsOnBranch(target, "main") || IsOnBranch(target, "master");

    private static bool IsOnBranch(string? target, string branch)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        return target.Equals(branch, StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"/{branch}", StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"\\{branch}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"v2.36.10.0", "2.36.10" or "2.36.10-beta" give a version; a tag without one gives null.</summary>
    internal static Version? TryParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var normalized = tagName.TrimStart('v', 'V');
        if (Version.TryParse(normalized, out var parsed))
            return parsed;

        var numericPart = new string(normalized
            .TakeWhile(c => char.IsDigit(c) || c == '.')
            .ToArray())
            .Trim('.');

        return Version.TryParse(numericPart, out parsed) ? parsed : null;
    }
}
