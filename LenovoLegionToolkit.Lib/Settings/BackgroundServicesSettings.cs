using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>Which service presets LLT keeps stopped while it runs (normal use, not only during games).</summary>
public class BackgroundServicesSettings : AbstractSettings<BackgroundServicesSettings.BackgroundServicesStore>
{
    private const string FILE_NAME = "background_services.json";

    public BackgroundServicesSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal BackgroundServicesSettings(string folder) : base(FILE_NAME, folder) { }

    public class BackgroundServicesStore
    {
        public bool Enabled { get; set; }

        /// <summary>Ids of the chosen presets (<see cref="GameProfiles.ServicePresets"/>).</summary>
        public List<string> Presets { get; set; } = [];
    }
}

/// <summary>
/// The services LLT stopped for normal use, written before each stop, so they are started again when LLT exits,
/// when the setting is turned off, or at the next start after LLT was ended. Not part of settings backups: an old
/// note restored later could start or keep stopped services from another time.
/// </summary>
public class BackgroundServicesSessionSettings : AbstractSettings<BackgroundServicesSessionSettings.BackgroundServicesSessionStore>
{
    private const string FILE_NAME = "background_services_session.json";

    public BackgroundServicesSessionSettings() : base(FILE_NAME) { }

    internal BackgroundServicesSessionSettings(string folder) : base(FILE_NAME, folder) { }

    public class BackgroundServicesSessionStore
    {
        public List<string> StoppedServices { get; set; } = [];
    }
}
