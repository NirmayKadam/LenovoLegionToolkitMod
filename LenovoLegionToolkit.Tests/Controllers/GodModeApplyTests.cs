using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using Xunit;
using Preset = LenovoLegionToolkit.Lib.Settings.GodModeSettings.GodModeSettingsStore.Preset;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// What Custom mode (God Mode) sends to the laptop on the data-driven path (current Legion/LOQ and non-gaming
/// models). The laptop is a scripted double that answers reads from data each test sets up and records every write;
/// the presets are real <c>godmode.json</c> files in a temporary folder. These tests cover LLT's decisions: what is
/// sent, in what order, what is held back. What the real firmware does with a value is on the hardware checklist.
/// </summary>
public sealed class GodModeApplyTests : IDisposable
{
    private const uint Mask = 0xFFFF00FF;

    private static readonly uint LongTerm = (uint)CapabilityID.CPULongTermPowerLimit & Mask;     // 0x01020000
    private static readonly uint ShortTerm = (uint)CapabilityID.CPUShortTermPowerLimit & Mask;   // 0x01010000
    private static readonly uint GpuBoost = (uint)CapabilityID.GPUPowerBoost & Mask;
    private static readonly uint FanFullSpeed = (uint)CapabilityID.FanFullSpeed & Mask;
    private static readonly uint NonGamingTgp = (uint)NonGamingCapabilityID.GPUConfigurableTGP & Mask;

    private static readonly ushort[] Curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-godmode-apply-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _presetId = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>A scripted laptop: reads come from the fields below, every write is recorded.</summary>
    private sealed class ScriptedLaptop : IGodModeFirmware
    {
        public MachineInformation Machine { get; set; }
        public SoftwareStatus Vantage { get; set; } = SoftwareStatus.NotFound;
        public SoftwareStatus LegionZone { get; set; } = SoftwareStatus.NotFound;
        public SoftwareStatus LegionSpace { get; set; } = SoftwareStatus.NotFound;

        /// <summary>The firmware's ranges and per-mode defaults.</summary>
        public List<RangeCapability> Capabilities { get; } = [];
        public int CapabilityReads { get; private set; }

        /// <summary>The capability read with this number (1 = first) throws, as a failed WMI call would.</summary>
        public int? CapabilityReadFailsOn { get; set; }

        public List<(uint Id, int Value)> FeatureWrites { get; } = [];

        /// <summary>The feature write to this id throws.</summary>
        public uint? FailingFeatureWrite { get; set; }

        public List<byte[]> FanTableWrites { get; } = [];

        /// <summary>What the fan table reads back as; null = what was asked for (the firmware kept it).</summary>
        public uint[]? FanTableReadBack { get; set; }
        public ushort[] ExpectedFanTable { get; set; } = Curve;

        public int BiosOcMode { get; set; }
        public List<(byte Mode, uint TuneId, int Value)> OcWrites { get; } = [];
        public int PowerSettingsCalls { get; private set; }

        public Task<MachineInformation> GetMachineInformationAsync() => Task.FromResult(Machine);
        public Task<SoftwareStatus> GetVantageStatusAsync() => Task.FromResult(Vantage);
        public Task<SoftwareStatus> GetLegionZoneStatusAsync() => Task.FromResult(LegionZone);
        public Task<SoftwareStatus> GetLegionSpaceStatusAsync() => Task.FromResult(LegionSpace);

        public Task<IEnumerable<RangeCapability>> ReadCapabilityDataAsync()
        {
            CapabilityReads++;
            if (CapabilityReads == CapabilityReadFailsOn)
                throw new InvalidOperationException("WMI call failed");
            return Task.FromResult<IEnumerable<RangeCapability>>(Capabilities.ToArray());
        }

        public Task<IEnumerable<DiscreteCapability>> ReadDiscreteDataAsync() => Task.FromResult<IEnumerable<DiscreteCapability>>([]);

        public Task<IEnumerable<(int mode, byte fanId, byte sensorId, ushort[] fanTableData, ushort[] sensorTableData)>> ReadFanTableDataAsync() =>
            Task.FromResult<IEnumerable<(int, byte, byte, ushort[], ushort[])>>([]);

        /// <summary>What each feature value reads as right now (by masked id); 0 when not set.</summary>
        public Dictionary<uint, int> FeatureValues { get; } = [];

        /// <summary>The feature read of this id throws, as a failed WMI call would.</summary>
        public uint? FailingFeatureRead { get; set; }

        public List<uint> FeatureReads { get; } = [];

        public Task<int> GetFeatureValueAsync(uint idRaw)
        {
            FeatureReads.Add(idRaw);
            if (idRaw == FailingFeatureRead)
                throw new InvalidOperationException($"Read of 0x{idRaw:X} failed");
            return Task.FromResult(FeatureValues.GetValueOrDefault(idRaw));
        }

        public Task SetFeatureValueAsync(uint idRaw, int value)
        {
            if (idRaw == FailingFeatureWrite)
                throw new InvalidOperationException($"Write to 0x{idRaw:X} failed");
            FeatureWrites.Add((idRaw, value));
            return Task.CompletedTask;
        }

        public Task<int> GetGpuPowerTargetOffsetAsync() => Task.FromResult(0);
        public Task SetGpuPowerTargetOffsetAsync(int offsetInWatts) => throw new InvalidOperationException("No NVIDIA GPU in this script");
        public Task ResetGpuPowerTargetOffsetAsync() => Task.CompletedTask;

        public Task<bool> GetFanFullSpeedAsync() => Task.FromResult(false);
        public Task SetFanFullSpeedAsync(int status) => throw new InvalidOperationException("Legion models switch full speed through the feature value");

        public Task SetFanTableAsync(byte[] fanTable)
        {
            FanTableWrites.Add(fanTable);
            return Task.CompletedTask;
        }

        public Task<uint[]> GetFanTableAsync(int fanId, int sensorId) =>
            Task.FromResult(FanTableReadBack ?? ExpectedFanTable.Select(v => (uint)v).ToArray());

        public Task<int> GetBiosOcModeAsync() => Task.FromResult(BiosOcMode);

        public Task SetCpuOcDataAsync(byte mode, uint tuneId, int value)
        {
            OcWrites.Add((mode, tuneId, value));
            return Task.CompletedTask;
        }

        public Task EnsureWindowsPowerSettingsAsync(Preset preset)
        {
            PowerSettingsCalls++;
            return Task.CompletedTask;
        }

        public Task DelayAsync(TimeSpan delay) => Task.CompletedTask;
    }

    private static MachineInformation Laptop(GodModePlatform platform, int smartFanVersion = 6, bool amd = false) => new()
    {
        Vendor = "LENOVO",
        MachineType = "82XV",
        Model = "Scripted laptop",
        SerialNumber = string.Empty,
        SmartFanVersion = smartFanVersion,
        SupportedPowerModes = [PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance, PowerModeState.GodMode],
        Properties = new MachineInformation.PropertyData { GodModePlatform = platform, IsAmdDevice = amd },
    };

    /// <summary>A Legion laptop whose firmware allows CPU long-term 15–80 W and short-term 15–100 W (both default 45),
    /// and whose Performance mode uses 55 W and 70 W.</summary>
    private static ScriptedLaptop LegionLaptop(int smartFanVersion = 6)
    {
        var laptop = new ScriptedLaptop { Machine = Laptop(GodModePlatform.Legion, smartFanVersion) };
        laptop.Capabilities.Add(new RangeCapability(CapabilityID.CPULongTermPowerLimit, 45, 15, 80, 1));
        laptop.Capabilities.Add(new RangeCapability(CapabilityID.CPUShortTermPowerLimit, 45, 15, 100, 1));
        laptop.Capabilities.Add(new RangeCapability(CapabilityID.GPUPowerBoost, 5, 0, 15, 1));
        laptop.Capabilities.Add(new RangeCapability(PerformanceDefaultId(CapabilityID.CPULongTermPowerLimit), 55, 0, 0, 0));
        laptop.Capabilities.Add(new RangeCapability(PerformanceDefaultId(CapabilityID.CPUShortTermPowerLimit), 70, 0, 0, 0));
        return laptop;
    }

    /// <summary>Where the firmware keeps a setting's Performance-mode default: the mode in the id's second byte.</summary>
    private static CapabilityID PerformanceDefaultId(CapabilityID id) => (CapabilityID)(((uint)id & Mask) + (((uint)PowerModeState.Performance + 1) << 8));

    private static StepperValue Saved(int value) => new(value, 0, 0, 0, [], null);

    private GodModeController Controller(ScriptedLaptop laptop, Preset preset)
    {
        var settings = new GodModeSettings(_dir);
        settings.Store.ActivePresetId = _presetId;
        settings.Store.Presets = new() { [_presetId] = preset };
        settings.SynchronizeStore();
        return new GodModeController(settings, laptop);
    }

    private static int IndexOf(ScriptedLaptop laptop, uint id) => laptop.FeatureWrites.FindIndex(w => w.Id == id);

    // ---- Other Lenovo software running ----

    [Theory]
    [InlineData("Vantage")]
    [InlineData("LegionZone")]
    public async Task OtherLenovoSoftwareRunning_NothingIsSent_AndNoChangeIsReported(string running)
    {
        var laptop = LegionLaptop();
        if (running == "Vantage") laptop.Vantage = SoftwareStatus.Enabled;
        else laptop.LegionZone = SoftwareStatus.Enabled;
        var controller = Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) });
        var changed = false;
        controller.PresetChanged += (_, _) => changed = true;

        await controller.ApplyStateAsync();

        Assert.Empty(laptop.FeatureWrites);
        Assert.False(changed);
    }

    [Theory]
    [InlineData(8, true)]    // Smart Fan 8 and later: Legion Space blocks the apply
    [InlineData(6, false)]   // older Smart Fan: Legion Space doesn't matter
    public async Task LegionSpaceRunning_BlocksOnlyFromSmartFan8(int smartFanVersion, bool blocked)
    {
        var laptop = LegionLaptop(smartFanVersion);
        laptop.LegionSpace = SoftwareStatus.Enabled;

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) }).ApplyStateAsync();

        Assert.Equal(blocked, laptop.FeatureWrites.Count == 0);
    }

    // ---- What is sent ----

    [Fact]
    public async Task SavedValuesInRange_AreSentAsIs_Masked_InTheFirmwaresOrder()
    {
        var laptop = LegionLaptop();
        var controller = Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60), CPUShortTermPowerLimit = Saved(90) });
        var changed = false;
        controller.PresetChanged += (_, _) => changed = true;

        await controller.ApplyStateAsync();

        Assert.Contains((LongTerm, 60), laptop.FeatureWrites);
        Assert.Contains((ShortTerm, 90), laptop.FeatureWrites);
        Assert.True(IndexOf(laptop, LongTerm) < IndexOf(laptop, ShortTerm));   // long-term comes first in the config
        Assert.True(changed);
        Assert.Equal(1, laptop.PowerSettingsCalls);
    }

    [Fact]
    public async Task ASavedValueOutsideTheRange_IsReplacedByTheFirmwaresDefault()
    {
        var laptop = LegionLaptop();

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(120) }).ApplyStateAsync();

        Assert.Contains((LongTerm, 45), laptop.FeatureWrites);
        Assert.DoesNotContain(laptop.FeatureWrites, w => w.Value == 120);
    }

    [Fact]
    public async Task TheMaxOffset_OnlyNarrowsTheRange()
    {
        // 75 W is inside the firmware's 15–80 W, but not inside 15–70 W once the max offset of −10 applies.
        var laptop = LegionLaptop();

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(75), MaxValueOffset = -10 }).ApplyStateAsync();

        Assert.Contains((LongTerm, 45), laptop.FeatureWrites);

        // A positive max offset can't widen it: 90 W stays out of range.
        var wider = LegionLaptop();
        await Controller(wider, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(90), MaxValueOffset = 20 }).ApplyStateAsync();
        Assert.Contains((LongTerm, 45), wider.FeatureWrites);
    }

    [Fact]
    public async Task OnLegion_AnUnsavedValue_GetsThePerformanceModeDefault()
    {
        var laptop = LegionLaptop();

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) }).ApplyStateAsync();

        Assert.Contains((ShortTerm, 70), laptop.FeatureWrites);   // short-term isn't saved: Performance's 70 W
    }

    [Fact]
    public async Task WhenTheRangesCantBeRead_ThePerformanceDefaultIsSent_NotTheSavedValue()
    {
        // Your choice for Round 7: the firmware's own mode default is the fallback. The defaults are read first
        // (read 1), the ranges second (read 2), which fails here.
        var laptop = LegionLaptop();
        laptop.CapabilityReadFailsOn = 2;

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) }).ApplyStateAsync();

        Assert.Contains((LongTerm, 55), laptop.FeatureWrites);
        Assert.DoesNotContain((LongTerm, 60), laptop.FeatureWrites);
    }

    [Fact]
    public async Task OnNonGaming_AnUnsavedValueIsNotSent_And_cTgpIsSentAsAnOffsetFromItsLowestStep()
    {
        var laptop = new ScriptedLaptop { Machine = Laptop(GodModePlatform.NonGaming) };

        await Controller(laptop, new Preset { Name = "Test", GPUConfigurableTGP = Saved(75) }).ApplyStateAsync();

        Assert.Equal([(NonGamingTgp, 20)], laptop.FeatureWrites);   // 75 W − the lowest step (55 W); nothing else
    }

    [Fact]
    public async Task OnNonGaming_LegionZoneAndSpaceDontBlock_AndNoFanTableIsWritten()
    {
        var laptop = new ScriptedLaptop { Machine = Laptop(GodModePlatform.NonGaming, smartFanVersion: 8), LegionZone = SoftwareStatus.Enabled, LegionSpace = SoftwareStatus.Enabled };

        await Controller(laptop, new Preset { Name = "Test", GPUConfigurableTGP = Saved(75), FanTable = new FanTable(Curve) }).ApplyStateAsync();

        Assert.NotEmpty(laptop.FeatureWrites);
        Assert.Empty(laptop.FanTableWrites);
    }

    // ---- Failures ----

    [Fact]
    public async Task ALimitThatFails_StopsTheLaterLimits_TheFanStepStillRuns_ThenTheErrorIsRaised()
    {
        var laptop = LegionLaptop();
        laptop.FailingFeatureWrite = LongTerm;
        var controller = Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60), CPUShortTermPowerLimit = Saved(90), FanTable = new FanTable(Curve) });
        var changed = false;
        controller.PresetChanged += (_, _) => changed = true;

        await Assert.ThrowsAsync<InvalidOperationException>(controller.ApplyStateAsync);

        Assert.DoesNotContain(laptop.FeatureWrites, w => w.Id == ShortTerm);   // later limits held back
        Assert.Single(laptop.FanTableWrites);                                   // the preset's fan curve still went out
        Assert.False(changed);
    }

    [Fact]
    public async Task AGpuSettingThatFails_IsSkipped_AndTheRestStillGoesOut()
    {
        var laptop = LegionLaptop();
        laptop.FailingFeatureWrite = GpuBoost;
        var controller = Controller(laptop, new Preset { Name = "Test", GPUPowerBoost = Saved(10), FanFullSpeed = true });
        var changed = false;
        controller.PresetChanged += (_, _) => changed = true;

        await controller.ApplyStateAsync();

        Assert.Contains((FanFullSpeed, 1), laptop.FeatureWrites);   // after the GPU settings in the config
        Assert.True(changed);
    }

    // ---- Fan ----

    [Fact]
    public async Task NoSavedFanCurve_NoFanTableIsWritten()
    {
        var laptop = LegionLaptop();

        await Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) }).ApplyStateAsync();

        Assert.Empty(laptop.FanTableWrites);
    }

    [Fact]
    public async Task AFanCurveTheFirmwareDoesntKeep_IsTriedThreeTimes_ThenReported()
    {
        var laptop = LegionLaptop();
        laptop.FanTableReadBack = [1, 1, 2, 3, 4, 5, 6, 7, 8, 10];   // the firmware's own curve stays

        await Assert.ThrowsAsync<FanTableNotKeptException>(Controller(laptop, new Preset { Name = "Test", FanTable = new FanTable(Curve) }).ApplyStateAsync);

        Assert.Equal(3, laptop.FanTableWrites.Count);
    }

    [Fact]
    public async Task AKeptFanCurve_IsWrittenOnce_AfterFullSpeedIsSwitchedOff()
    {
        var laptop = LegionLaptop();

        await Controller(laptop, new Preset { Name = "Test", FanTable = new FanTable(Curve) }).ApplyStateAsync();

        Assert.Single(laptop.FanTableWrites);
        Assert.Contains((FanFullSpeed, 0), laptop.FeatureWrites);
    }

    // ---- AMD overclocking ----

    [Fact]
    public async Task AmdOverclockValues_AreHeldToTheirFixedRanges()
    {
        var laptop = LegionLaptop();
        laptop.BiosOcMode = 3;   // overclocking enabled in BIOS
        var preset = new Preset
        {
            Name = "Test",
            EnableOverclocking = true,
            PrecisionBoostOverdriveScaler = Saved(50),
            PrecisionBoostOverdriveBoostFrequency = Saved(900),
            AllCoreCurveOptimizer = Saved(-5),
            EnableAllCoreCurveOptimizer = true,
        };

        await Controller(laptop, preset).ApplyStateAsync();

        Assert.Equal([7, 200, 0], laptop.OcWrites.Select(w => w.Value));
    }

    [Fact]
    public async Task AmdOverclocking_NotEnabledInBios_SendsNothing()
    {
        var laptop = LegionLaptop();
        laptop.BiosOcMode = 0;

        await Controller(laptop, new Preset { Name = "Test", EnableOverclocking = true, PrecisionBoostOverdriveScaler = Saved(3) }).ApplyStateAsync();

        Assert.Empty(laptop.OcWrites);
        Assert.DoesNotContain(laptop.FeatureWrites, w => w.Id == (uint)CapabilityID.CPUOverclockingEnable);
    }

    // ---- Ranges cache ----

    [Fact]
    public async Task TheRangesAreReadOnce_UntilForgotten()
    {
        var laptop = LegionLaptop();
        var controller = Controller(laptop, new Preset { Name = "Test", CPULongTermPowerLimit = Saved(60) });

        await controller.ApplyStateAsync();
        var afterFirst = laptop.CapabilityReads;    // the mode defaults and the ranges
        await controller.ApplyStateAsync();
        var afterSecond = laptop.CapabilityReads;   // the mode defaults only
        controller.ForgetFirmwareRanges();
        await controller.ApplyStateAsync();

        Assert.Equal(2, afterFirst);
        Assert.Equal(afterFirst + 1, afterSecond);
        Assert.Equal(afterSecond + 2, laptop.CapabilityReads);
    }

    // ---- Current values (the Dashboard's "now" column) ----

    private static readonly string[] LimitProperties =
        [nameof(GodModePreset.CPULongTermPowerLimit), nameof(GodModePreset.CPUShortTermPowerLimit), nameof(GodModePreset.GPUPowerBoost)];

    [Fact]
    public async Task CurrentValues_OnCurrentModels_AreReadAsGodModeReadsThem()
    {
        // The LOQ's log showed Lenovo's older CPU/GPU methods missing; these models answer through feature values.
        var laptop = LegionLaptop();
        laptop.FeatureValues[LongTerm] = 55;
        laptop.FeatureValues[ShortTerm] = 70;
        laptop.FeatureValues[GpuBoost] = 15;
        var controller = Controller(laptop, new Preset { Name = "Test" });

        var current = await controller.ReadCurrentValuesAsync(LimitProperties);

        Assert.NotNull(current);
        Assert.Equal(55, current.Values[nameof(GodModePreset.CPULongTermPowerLimit)]);
        Assert.Equal(70, current.Values[nameof(GodModePreset.CPUShortTermPowerLimit)]);
        Assert.Equal(15, current.Values[nameof(GodModePreset.GPUPowerBoost)]);
        Assert.Empty(current.Failures);
        Assert.Contains(LongTerm, laptop.FeatureReads);
        Assert.Contains(ShortTerm, laptop.FeatureReads);
        Assert.Contains(GpuBoost, laptop.FeatureReads);
    }

    [Fact]
    public async Task CurrentValues_OnlyTheAskedOnesAreRead_AndAFailureIsReportedNotGuessed()
    {
        var laptop = LegionLaptop();
        laptop.FeatureValues[ShortTerm] = 70;
        laptop.FailingFeatureRead = LongTerm;
        var controller = Controller(laptop, new Preset { Name = "Test" });

        var current = await controller.ReadCurrentValuesAsync([nameof(GodModePreset.CPULongTermPowerLimit), nameof(GodModePreset.CPUShortTermPowerLimit)]);

        Assert.NotNull(current);
        Assert.False(current.Values.ContainsKey(nameof(GodModePreset.CPULongTermPowerLimit)));
        Assert.IsType<InvalidOperationException>(current.Failures[nameof(GodModePreset.CPULongTermPowerLimit)]);
        Assert.Equal(70, current.Values[nameof(GodModePreset.CPUShortTermPowerLimit)]);
        Assert.DoesNotContain(GpuBoost, laptop.FeatureReads);
    }

    [Fact]
    public async Task CurrentValues_OnOlderModels_AreLeftToTheOlderMethods()
    {
        var laptop = new ScriptedLaptop { Machine = Laptop(GodModePlatform.LegacyLegion) };
        var controller = Controller(laptop, new Preset { Name = "Test" });

        Assert.Null(await controller.ReadCurrentValuesAsync(LimitProperties));
        Assert.Empty(laptop.FeatureReads);
    }

    [Fact]
    public void EveryValueTheDashboardShows_HasAPresetPropertyToReadItFrom()
    {
        var properties = typeof(GodModePreset).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Equal(Enum.GetValues<LenovoLegionToolkit.Lib.Controllers.PowerModeValueKind>().Order(),
            LenovoLegionToolkit.Lib.Controllers.PowerModeProfileReader.KindByPresetProperty.Values.Order());
        Assert.All(LenovoLegionToolkit.Lib.Controllers.PowerModeProfileReader.KindByPresetProperty.Keys, name => Assert.Contains(name, properties));
    }
}
