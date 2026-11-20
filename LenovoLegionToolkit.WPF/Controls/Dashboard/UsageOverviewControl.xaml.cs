using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// CPU, GPU and RAM use as rings, and the last 60 seconds of CPU or GPU temperature. Reads only while shown, at the
/// Dashboard's sensor interval, from the same shared readings as the sensor cards, so it adds no hardware reads.
/// </summary>
public partial class UsageOverviewControl
{
    private const double RingSize = 72;
    private const double RingThickness = 6;
    private const double CautionAt = 90;
    private const double GraphMin = 0, GraphMax = 100;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly SensorsController _sensors = IoCContainer.Resolve<SensorsController>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    private readonly ReadingHistory _cpuTemperatures = new(Window);
    private readonly ReadingHistory _gpuTemperatures = new(Window);

    private CancellationTokenSource? _cts;

    public UsageOverviewControl()
    {
        InitializeComponent();
        IsVisibleChanged += UsageOverviewControl_IsVisibleChanged;
    }

    private void UsageOverviewControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _cts?.Cancel();
        _cts = null;

        if (!IsVisible)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoopAsync(token), token);
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var sensorsSupported = false;
        try
        {
            sensorsSupported = await _sensors.IsSupportedAsync().ConfigureAwait(false);
            if (sensorsSupported)
                await _sensors.PrepareAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Usage overview: sensors unavailable.", ex);
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                SensorsData? data = null;
                if (sensorsSupported)
                {
                    try
                    {
                        data = await _sensors.GetDataAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Usage overview: sensor read failed.", ex);
                    }
                }

                var memory = SystemMemory.GetStatus();
                var now = DateTime.UtcNow;
                Dispatcher.Post(() => Show(data, memory, now));

                await Task.Delay(TimeSpan.FromSeconds(_dashboardSettings.Store.SensorsRefreshIntervalSeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Usage overview refresh failed.", ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void Show(SensorsData? data, (double LoadPercent, double UsedGb, double TotalGb)? memory, DateTime now)
    {
        var cpuUse = Known(data?.CPU.Utilization);
        var gpuUse = Known(data?.GPU.Utilization);
        _cpuTemperatures.Add(now, Known(data?.CPU.Temperature));
        _gpuTemperatures.Add(now, Known(data?.GPU.Temperature));

        SetRing(_cpuArc, _cpuValue, cpuUse, cpuUse is { } c ? $"{c:0}%" : "–");
        SetRing(_gpuArc, _gpuValue, gpuUse, gpuUse is { } g ? $"{g:0}%" : "–");

        if (memory is { } m)
        {
            SetRing(_ramArc, _ramValue, m.LoadPercent, $"{m.LoadPercent:0}%");
            _ramLabel.Text = string.Format(Resource.UsageOverview_RamLabel, m.UsedGb.ToString("0.0", CultureInfo.CurrentCulture), m.TotalGb.ToString("0", CultureInfo.CurrentCulture));
        }
        else
        {
            SetRing(_ramArc, _ramValue, null, "–");
            _ramLabel.Text = Resource.UsageOverview_Ram;
        }

        DrawGraph(now);
    }

    /// <summary>The sensors report -1 for a value they don't know (for example while the dGPU sleeps).</summary>
    private static double? Known(int? value) => value is { } v && v >= 0 ? v : null;

    private void SetRing(Path arc, System.Windows.Controls.TextBlock text, double? percent, string valueText)
    {
        text.Text = valueText;

        var radius = (RingSize - RingThickness) / 2;
        var offset = RingThickness / 2;
        var ring = RingGeometry.Arc(percent ?? 0, radius);

        arc.Data = ring switch
        {
            { Empty: true } => null,
            { Full: true } => new EllipseGeometry(new Point(radius + offset, radius + offset), radius, radius),
            _ => new PathGeometry([new PathFigure(Shift(ring.Start, offset), [new ArcSegment(Shift(ring.End, offset), new Size(radius, radius), 0, ring.IsLargeArc, SweepDirection.Clockwise, true)], false)])
        };

        arc.SetResourceReference(Shape.StrokeProperty, percent >= CautionAt ? "SystemFillColorCautionBrush" : "SystemAccentColorSecondaryBrush");
    }

    private static Point Shift(Point point, double by) => new(point.X + by, point.Y + by);

    private void TemperatureSource_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            DrawGraph(DateTime.UtcNow);
    }

    private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph(DateTime.UtcNow);

    private void DrawGraph(DateTime now)
    {
        var history = _gpuTemperatureButton.IsChecked == true ? _gpuTemperatures : _cpuTemperatures;
        var readings = history.Readings(now);

        var latest = readings.Count > 0 ? readings[^1].Value : null;
        _temperatureValue.Text = latest is { } t ? string.Format(Resource.UsageOverview_TemperatureValue, t.ToString("0", CultureInfo.CurrentCulture)) : "–";

        _graphCanvas.Children.Clear();
        var width = _graphCanvas.ActualWidth;
        var height = _graphCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        foreach (var line in GraphLayout.Map(readings, now, Window, width, height, GraphMin, GraphMax))
        {
            var polyline = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Points = new PointCollection(line) };
            polyline.SetResourceReference(Shape.StrokeProperty, latest >= CautionAt ? "SystemFillColorCautionBrush" : "SystemAccentColorSecondaryBrush");
            _graphCanvas.Children.Add(polyline);
        }
    }
}
