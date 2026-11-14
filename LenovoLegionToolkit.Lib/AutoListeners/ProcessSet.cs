using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace LenovoLegionToolkit.Lib.AutoListeners;

/// <summary>
/// A set of running processes, keyed by the process id read once, when the process is added.
/// <para>
/// The game listener used a <c>HashSet&lt;Process&gt;</c> hashed on <see cref="Process.Id"/>. Once a
/// <see cref="Process"/> object has been disposed, reading its <c>Id</c> throws "No process is associated with
/// this object", so looking up or removing a process that another exit event had already cleaned up threw on a
/// thread-pool thread and ended LLT (seen in the Windows log when games closed). This set never reads <c>Id</c>
/// from an object it can't trust: it uses the id stored when the process was added, or the object itself.
/// </para>
/// </summary>
internal sealed class ProcessSet : IEnumerable<Process>
{
    private readonly Dictionary<int, Process> _byId = [];

    public int Count => _byId.Count;

    /// <summary>Adds the process; false if one with the same id is already there or its id can't be read.</summary>
    public bool Add(Process process) => TryGetId(process, out var id) && _byId.TryAdd(id, process);

    /// <summary>Whether this process (this object, or another object for the same id) is in the set.</summary>
    public bool Contains(Process process) => FindKey(process) is not null;

    /// <summary>Removes this process (this object, or another object for the same id). Never throws.</summary>
    public bool Remove(Process process) => FindKey(process) is { } key && _byId.Remove(key);

    public void Clear() => _byId.Clear();

    private int? FindKey(Process process)
    {
        if (TryGetId(process, out var id) && _byId.ContainsKey(id))
            return id;

        // The object can't tell its id any more (disposed): find it by identity.
        foreach (var (key, value) in _byId)
        {
            if (ReferenceEquals(value, process))
                return key;
        }

        return null;
    }

    private static bool TryGetId(Process process, out int id)
    {
        try
        {
            id = process.Id;
            return true;
        }
        catch (InvalidOperationException)
        {
            id = 0;
            return false;
        }
    }

    public IEnumerator<Process> GetEnumerator() => _byId.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
