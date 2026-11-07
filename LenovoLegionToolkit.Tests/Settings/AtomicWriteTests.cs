using System;
using System.IO;
using System.Text;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

// Works on real files in a temporary folder.
public class AtomicWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");

    public AtomicWriteTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void CreatesNewFile()
    {
        var path = Path.Combine(_dir, "settings.json");

        AbstractSettings<object>.WriteAllTextAtomic(path, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(path));
    }

    [Fact]
    public void ReplacesExistingFile_AndLeavesNoTempFiles()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "old contents that are longer than the new ones");

        AbstractSettings<object>.WriteAllTextAtomic(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void NonAsciiContent_IsPreserved()
    {
        var path = Path.Combine(_dir, "settings.json");
        const string contents = "{\"name\":\"游戏模式 – Performance\"}";

        AbstractSettings<object>.WriteAllTextAtomic(path, contents);

        Assert.Equal(contents, File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void FailedWrite_KeepsOriginalFile_AndCleansUpTemp()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "original");

        // Holding the target open without delete sharing makes the final replace fail.
        // Windows reports this as access denied (UnauthorizedAccessException); a sharing violation would be an IOException.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = Record.Exception(() => AbstractSettings<object>.WriteAllTextAtomic(path, "new"));
            Assert.True(ex is IOException or UnauthorizedAccessException, $"Unexpected exception: {ex?.GetType().Name ?? "none"}");
        }

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }
}
