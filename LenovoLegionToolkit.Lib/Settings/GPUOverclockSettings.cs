using static LenovoLegionToolkit.Lib.Settings.GPUOverclockSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class GPUOverclockSettings : AbstractSettings<GPUOverclockSettingsStore>
{
    private const string FILE_NAME = "gpu_oc.json";

    public GPUOverclockSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal GPUOverclockSettings(string folder) : base(FILE_NAME, folder) { }

    public class GPUOverclockSettingsStore
    {
        public bool Enabled { get; set; }
        public GPUOverclockInfo Info { get; set; } = GPUOverclockInfo.Zero;

        /// <summary>
        /// Values that were being applied and haven't proven stable yet. Still set at the next start means that
        /// apply was never confirmed (see <c>OverclockApplyGuard</c>).
        /// </summary>
        public GPUOverclockInfo? PendingInfo { get; set; }

        /// <summary>The last values the app stayed up with long enough to call stable.</summary>
        public GPUOverclockInfo? StableInfo { get; set; }

        /// <summary>The overclock was switched off because a previous apply was never confirmed.</summary>
        public bool DisabledAfterInterruptedApply { get; set; }
    }
}
