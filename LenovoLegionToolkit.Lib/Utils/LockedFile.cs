using System.IO;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Opens a file so that, while the handle is held, nobody can write to, replace or delete it,
/// but it can still be read and executed. Used to make sure the file LLT verifies is the file it runs,
/// even when it sits in a folder other programs can write to (for example %TEMP%).
/// </summary>
public static class LockedFile
{
    public static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
}
