using System.Collections.Generic;
using LenovoLegionToolkit.Lib.GameSessions;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// The last game sessions (newest first), for the Game sessions card. History, not a setting: not part of settings
/// backups.
/// </summary>
public class GameSessionSettings : AbstractSettings<GameSessionSettings.GameSessionStore>
{
    private const string FILE_NAME = "game_sessions.json";

    /// <summary>How many sessions are kept; older ones are dropped.</summary>
    public const int MaxSessions = 30;

    public GameSessionSettings() : base(FILE_NAME) { }

    internal GameSessionSettings(string folder) : base(FILE_NAME, folder) { }

    public class GameSessionStore
    {
        public List<GameSessionReport> Sessions { get; set; } = [];
    }

    /// <summary>Adds a session at the front, drops the oldest beyond <see cref="MaxSessions"/>, and saves.</summary>
    public void Add(GameSessionReport report)
    {
        Store.Sessions.Insert(0, report);
        if (Store.Sessions.Count > MaxSessions)
            Store.Sessions.RemoveRange(MaxSessions, Store.Sessions.Count - MaxSessions);
        SynchronizeStore();
    }
}
