using System;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

// The query helper every firmware/WMI read goes through, run against a real Windows class (no Lenovo hardware
// needed). It releases each WMI object right after reading it, so the results must already be plain values.
public class WmiQueryTests
{
    [Fact]
    public async Task ReadsARealWindowsClass()
    {
        var captions = await WMI.QueryAsync(@"root\CIMV2", "SELECT Caption FROM Win32_OperatingSystem", mo => mo["Caption"]?.ToString());

        var caption = Assert.Single(captions);
        Assert.Contains("Windows", caption);
    }

    [Fact]
    public async Task ResultsStayUsable_AfterTheWmiObjectsAreReleased()
    {
        // A list, not a lazy sequence over objects that are already released.
        var rows = await WMI.QueryAsync(@"root\CIMV2", "SELECT Name, ProcessId FROM Win32_Process WHERE ProcessId = 4", mo => (Name: mo["Name"]?.ToString(), Id: Convert.ToUInt32(mo["ProcessId"])));

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var row = Assert.Single(rows);
        Assert.Equal(4u, row.Id);
        Assert.Equal("System", row.Name);
    }

    [Fact]
    public async Task QueryForAMissingClass_FailsAsBefore()
    {
        await Assert.ThrowsAsync<ManagementException>(() => WMI.QueryAsync(@"root\CIMV2", "SELECT * FROM LLT_No_Such_Class", mo => mo));
    }
}
