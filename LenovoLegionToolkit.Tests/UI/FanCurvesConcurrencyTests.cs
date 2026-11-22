using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The fan curve settings are changed by the Fan curves window (UI thread) while a power-mode change reads them on
/// the listener's thread. Real settings files in a temporary folder.
/// </summary>
public sealed class FanCurvesConcurrencyTests : IDisposable
{
    private static readonly ushort[] Curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-fancurves-concurrency-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ReadingTheCurves_WhileTheWindowChangesThem_NeverFails()
    {
        var settings = new FanProfileSettings(_dir);
        settings.Store.Enabled = true;
        // Only the settings are read here; no fan table is read or written, so no table access is given.
        var controller = new FanProfileController(settings, null!, () => Task.FromResult(PowerModeState.Balance));
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // The window adding and removing curves for every mode.
        var writer = Task.Run(() =>
        {
            var modes = new[] { PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance };
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                var mode = modes[i % modes.Length];
                if (i % 2 == 0)
                    settings.Store.Curves[mode] = Curve;
                else
                    settings.Store.Curves.TryRemove(mode, out _);
            }
        });

        // A mode change and the undo snapshot reading them at the same time.
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = FanProfileController.CurveFor(settings.Store, PowerModeState.Balance);
                _ = controller.Snapshot();
            }
        });

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public void CurvesSavedBeforeThisChange_StillLoad()
    {
        // The file format is unchanged: a plain JSON object per mode.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "fan_profiles.json"), """
            {
              "Enabled": true,
              "Curves": {
                "Quiet": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
              }
            }
            """);

        var settings = new FanProfileSettings(_dir);

        Assert.True(settings.Store.Enabled);
        Assert.Equal(Curve, settings.Store.Curves[PowerModeState.Quiet]);

        settings.SynchronizeStore();
        Assert.Equal(Curve, new FanProfileSettings(_dir).Store.Curves[PowerModeState.Quiet]);
    }
}
