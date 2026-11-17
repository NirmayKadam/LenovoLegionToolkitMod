using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// What to do while a chosen game runs: force game-safe mode, ease listed apps (efficiency mode and lower
/// priority), close listed apps, and pause listed services. Everything is undone when the game ends.
/// </summary>
public class GameProfileSettings : AbstractSettings<GameProfileSettings.GameProfileSettingsStore>
{
    private const string FILE_NAME = "game_profiles.json";

    public GameProfileSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal GameProfileSettings(string folder) : base(FILE_NAME, folder) { }

    public class GameProfileSettingsStore
    {
        public List<GameProfile> Profiles { get; set; } = [];
    }
}

public class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shown in the list and in notifications.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The game's file name without ".exe", matched (ignoring case) against the names Windows reports for started processes.</summary>
    public string ExeName { get; set; } = string.Empty;

    /// <summary>Where the .exe was picked from; shown only.</summary>
    public string? ExePath { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Game-safe mode while this game runs, even without a detected anti-cheat and with the setting off.</summary>
    public bool ForceGameSafe { get; set; }

    /// <summary>Apps (file names without ".exe") put in efficiency mode with a lower priority while the game runs.</summary>
    public List<string> EfficiencyApps { get; set; } = [];

    /// <summary>Apps (file names without ".exe") asked to close, like clicking X, when the game starts. Not reopened.</summary>
    public List<string> CloseApps { get; set; } = [];

    /// <summary>Windows services stopped while the game runs and started again afterwards.</summary>
    public List<string> PauseServices { get; set; } = [];

    /// <summary>The user confirmed, once, what this profile does to other apps and services.</summary>
    public bool ChangesConfirmed { get; set; }
}

/// <summary>
/// What a running game profile changed, written before each change, so everything can be put back when the game
/// ends, when LLT exits, or at the next start if LLT was ended while the game ran.
/// </summary>
public class GameProfileSessionSettings : AbstractSettings<GameProfileSessionSettings.GameProfileSessionStore>
{
    private const string FILE_NAME = "game_profile_session.json";

    public GameProfileSessionSettings() : base(FILE_NAME) { }

    internal GameProfileSessionSettings(string folder) : base(FILE_NAME, folder) { }

    public class GameProfileSessionStore
    {
        /// <summary>The profile whose changes are in effect; null when nothing needs putting back.</summary>
        public Guid? ProfileId { get; set; }

        public string? GameName { get; set; }

        public List<string> PausedServices { get; set; } = [];

        public List<EasedApp> EasedApps { get; set; } = [];
    }
}

/// <summary>One app LLT eased, and what it had before.</summary>
public class EasedApp
{
    public int ProcessId { get; set; }

    /// <summary>With the id, identifies the process: a later process that reuses the id is never touched.</summary>
    public DateTime StartTimeUtc { get; set; }

    public string ExeName { get; set; } = string.Empty;

    public ProcessPriorityClass OriginalPriority { get; set; }

    /// <summary>The efficiency-mode setting the app had (power-throttling masks), when LLT changed it; null when it didn't.</summary>
    public uint? OriginalThrottleControlMask { get; set; }

    public uint? OriginalThrottleStateMask { get; set; }
}
