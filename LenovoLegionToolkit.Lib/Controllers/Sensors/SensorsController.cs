using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsController(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private enum ValueSource
    {
        None,
        Capability,
        FanMethod
    }

    private enum MaxFanSpeedSource
    {
        CurrentTable,
        DefaultTable,
        FanTestData
    }

    private readonly record struct FanTableEndpoint(int SensorId, int FanId);

    private sealed record Profile(
        FanTableEndpoint Cpu,
        FanTableEndpoint Gpu,
        FanTableEndpoint[] Required,
        SystemFanDefinition[] SystemFans,
        ValueSource TemperatureSource,
        ValueSource FanSpeedSource,
        MaxFanSpeedSource MaxFanSpeedSource,
        int CpuTemperatureSensorId,
        int GpuTemperatureSensorId,
        bool HasMotherboardTemperature);

    private static readonly Profile[] FanTableProfiles =
    [
        new(
            Cpu: new(4, 1),
            Gpu: new(5, 2),
            Required: [],
            SystemFans: [new(1, 4, CapabilityID.SystemCurrentFanSpeed, 1), new(5, 5, CapabilityID.ExtraSystemCurrentFanSpeed, 2)],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasMotherboardTemperature: true),
        new(
            Cpu: new(1, 1),
            Gpu: new(5, 2),
            Required: [],
            SystemFans: [new(4, 4, CapabilityID.SystemCurrentFanSpeed, 1)],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasMotherboardTemperature: true),
        new(
            Cpu: new(1, 1),
            Gpu: new(5, 2),
            Required: [new(4, 1)],
            SystemFans: [],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasMotherboardTemperature: true),
        new(
            Cpu: new(4, 1),
            Gpu: new(5, 2),
            Required: [],
            SystemFans: [],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasMotherboardTemperature: false),
        new(
            Cpu: new(3, 0),
            Gpu: new(4, 1),
            Required: [],
            SystemFans: [],
            TemperatureSource: ValueSource.FanMethod,
            FanSpeedSource: ValueSource.FanMethod,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 3,
            GpuTemperatureSensorId: 4,
            HasMotherboardTemperature: false),
        new(
            Cpu: new(0, 0),
            Gpu: new(0, 1),
            Required: [],
            SystemFans: [],
            TemperatureSource: ValueSource.FanMethod,
            FanSpeedSource: ValueSource.FanMethod,
            MaxFanSpeedSource: MaxFanSpeedSource.DefaultTable,
            CpuTemperatureSensorId: 3,
            GpuTemperatureSensorId: 4,
            HasMotherboardTemperature: false)
    ];

    private static readonly Profile FanTestDataProfile = new(
        Cpu: new(0, 1),
        Gpu: new(0, 2),
        Required: [],
        SystemFans: [],
        TemperatureSource: ValueSource.None,
        FanSpeedSource: ValueSource.Capability,
        MaxFanSpeedSource: MaxFanSpeedSource.FanTestData,
        CpuTemperatureSensorId: 0,
        GpuTemperatureSensorId: 0,
        HasMotherboardTemperature: false);

    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private Profile? _profile;

    // A laptop without a known fan table is checked again at most once a minute, not on every read.
    private static readonly TimeSpan RecheckUnsupportedAfter = TimeSpan.FromMinutes(1);
    private long _unsupportedAtMs = long.MinValue;

    // The dashboard sensors, the status strip and the HWiNFO integration all read these every second. Callers
    // within 750 ms (less than the shortest refresh interval, 1 s) share one real read.
    private static readonly TimeSpan SharedFor = TimeSpan.FromMilliseconds(750);
    private readonly SharedReading<SensorsData> _data = new(SharedFor);
    private readonly SharedReading<FanSpeedTable> _fanSpeeds = new(SharedFor);

    protected override IReadOnlyList<SystemFanDefinition> SystemFanDefinitions => _profile?.SystemFans ?? [];

    public override async Task<bool> IsSupportedAsync()
    {
        await _initSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_profile is not null)
            {
                return true;
            }

            if (_unsupportedAtMs != long.MinValue && Environment.TickCount64 - _unsupportedAtMs < RecheckUnsupportedAfter.TotalMilliseconds)
            {
                return false;
            }

            var profile = await FindFanTableProfileAsync().ConfigureAwait(false);
            if (profile is null && await HasFanTestDataAsync().ConfigureAwait(false))
            {
                profile = FanTestDataProfile;
            }

            if (profile is null)
            {
                _unsupportedAtMs = Environment.TickCount64;
                Log.Instance.Trace($"No known fan table layout found. [type={GetType().Name}]");
                return false;
            }

            _profile = profile;
            _unsupportedAtMs = long.MinValue;
            Log.Instance.Trace($"Sensor profile selected. [cpu={profile.Cpu}, gpu={profile.Gpu}, systemFans={profile.SystemFans.Length}, temperatures={profile.TemperatureSource}, fanSpeeds={profile.FanSpeedSource}, maxSpeeds={profile.MaxFanSpeedSource}]");
            _ = await base.GetFanSpeedsAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _profile = null;
            _unsupportedAtMs = Environment.TickCount64;
            Log.Instance.Trace($"Error checking support. [type={GetType().Name}]", ex);
            return false;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task<ISensorsController?> GetControllerAsync() =>
        await IsSupportedAsync().ConfigureAwait(false) ? this : null;

    public override Task<SensorsData> GetDataAsync() => _data.GetAsync(ReadDataAsync);

    public override Task<FanSpeedTable> GetFanSpeedsAsync() => _fanSpeeds.GetAsync(ReadFanSpeedsAsync);

    private async Task<SensorsData> ReadDataAsync()
    {
        if (!await IsSupportedAsync().ConfigureAwait(false))
        {
            return SensorsData.Empty;
        }

        return await base.GetDataAsync().ConfigureAwait(false);
    }

    private async Task<FanSpeedTable> ReadFanSpeedsAsync()
    {
        if (!await IsSupportedAsync().ConfigureAwait(false))
        {
            return new FanSpeedTable(-1, -1, []);
        }

        return await base.GetFanSpeedsAsync().ConfigureAwait(false);
    }

    protected override Task<int> GetCpuCurrentTemperatureAsync() =>
        GetTemperatureAsync(GetProfile().TemperatureSource, CapabilityID.CpuCurrentTemperature, GetProfile().CpuTemperatureSensorId);

    protected override Task<int> GetGpuCurrentTemperatureAsync() =>
        GetTemperatureAsync(GetProfile().TemperatureSource, CapabilityID.GpuCurrentTemperature, GetProfile().GpuTemperatureSensorId);

    protected override Task<int> GetMotherboardTemperatureAsync() => GetProfile().HasMotherboardTemperature
        ? GetCapabilityValueAsync(CapabilityID.MotherboardCurrentTemperature)
        : Task.FromResult(-1);

    protected override Task<int> GetCpuCurrentFanSpeedAsync() =>
        GetFanSpeedAsync(GetProfile().FanSpeedSource, CapabilityID.CpuCurrentFanSpeed, GetProfile().Cpu.FanId);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() =>
        GetFanSpeedAsync(GetProfile().FanSpeedSource, CapabilityID.GpuCurrentFanSpeed, GetProfile().Gpu.FanId);

    protected override Task<int> GetSystemFanSpeedAsync(SystemFanDefinition definition) =>
        GetFanSpeedAsync(GetProfile().FanSpeedSource, definition.SpeedCapability, definition.FanId);

    protected override Task<int> GetSystemFanMaxSpeedAsync(SystemFanDefinition definition) =>
        GetMaxFanSpeedAsync(GetProfile(), new FanTableEndpoint(definition.SensorId, definition.FanId));

    protected override Task<int> GetCpuMaxFanSpeedAsync() => GetMaxFanSpeedAsync(GetProfile(), GetProfile().Cpu);

    protected override Task<int> GetGpuMaxFanSpeedAsync() => GetMaxFanSpeedAsync(GetProfile(), GetProfile().Gpu);

    private static async Task<Profile?> FindFanTableProfileAsync()
    {
        try
        {
            var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
            return SelectFanTableProfile(data.Select(item => ((int)item.sensorId, (int)item.fanId)));
        }
        catch
        {
            return null;
        }
    }

    private static Profile? SelectFanTableProfile(IEnumerable<(int sensorId, int fanId)> data)
    {
        var index = SelectFanTableProfileIndex(data, out var systemFanIndexes);
        if (index < 0)
        {
            return null;
        }

        var profile = FanTableProfiles[index];
        return profile with { SystemFans = systemFanIndexes.Select(i => profile.SystemFans[i]).ToArray() };
    }

    /// <summary>
    /// The first known fan table layout whose CPU, GPU and required entries are all in <paramref name="data"/> (the
    /// sensor and fan IDs the firmware lists), or -1. A layout with system fans needs at least one of them present;
    /// <paramref name="systemFanIndexes"/> are the ones that are.
    /// </summary>
    internal static int SelectFanTableProfileIndex(IEnumerable<(int sensorId, int fanId)> data, out int[] systemFanIndexes)
    {
        var endpoints = data.Select(item => new FanTableEndpoint(item.sensorId, item.fanId)).ToHashSet();

        for (var i = 0; i < FanTableProfiles.Length; i++)
        {
            var profile = FanTableProfiles[i];
            if (!GetRequiredEndpoints(profile).All(endpoints.Contains))
            {
                continue;
            }

            var present = Enumerable.Range(0, profile.SystemFans.Length)
                .Where(f => endpoints.Contains(new FanTableEndpoint(profile.SystemFans[f].SensorId, profile.SystemFans[f].FanId)))
                .ToArray();

            if (profile.SystemFans.Length > 0 && present.Length == 0)
            {
                continue;
            }

            systemFanIndexes = present;
            return i;
        }

        systemFanIndexes = [];
        return -1;
    }

    private static async Task<bool> HasFanTestDataAsync()
    {
        try
        {
            return await WMI.LenovoFanTestData.ExistsAsync(FanTestDataProfile.Cpu.FanId).ConfigureAwait(false) ||
                   await WMI.LenovoFanTestData.ExistsAsync(FanTestDataProfile.Gpu.FanId).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> GetTemperatureAsync(ValueSource source, CapabilityID capabilityId, int sensorId)
    {
        var value = source switch
        {
            ValueSource.None => -1,
            ValueSource.Capability => await WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId).ConfigureAwait(false),
            ValueSource.FanMethod => await WMI.LenovoFanMethod.FanGetCurrentSensorTemperatureAsync(sensorId).ConfigureAwait(false),
            _ => -1
        };

        return value < 1 ? -1 : value;
    }

    private static Task<int> GetFanSpeedAsync(ValueSource source, CapabilityID capabilityId, int fanId) => source switch
    {
        ValueSource.Capability => WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId),
        ValueSource.FanMethod => WMI.LenovoFanMethod.FanGetCurrentFanSpeedAsync(fanId),
        _ => Task.FromResult(-1)
    };

    private static Task<int> GetCapabilityValueAsync(CapabilityID capabilityId) =>
        WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId);

    private static Task<int> GetMaxFanSpeedAsync(Profile profile, FanTableEndpoint endpoint) => profile.MaxFanSpeedSource switch
    {
        MaxFanSpeedSource.CurrentTable => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId),
        MaxFanSpeedSource.DefaultTable => WMI.LenovoFanMethod.GetDefaultFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId),
        MaxFanSpeedSource.FanTestData => WMI.LenovoFanTestData.GetFanMaxSpeedAsync(endpoint.FanId),
        _ => Task.FromResult(-1)
    };

    private Profile GetProfile() => _profile ?? throw new InvalidOperationException("No sensor profile selected");

    private static IEnumerable<FanTableEndpoint> GetRequiredEndpoints(Profile profile)
    {
        yield return profile.Cpu;
        yield return profile.Gpu;

        foreach (var endpoint in profile.Required)
        {
            yield return endpoint;
        }
    }
}
