using System;
using System.Management;
using LenovoLegionToolkit.Lib.Controllers;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// A WMI method the firmware doesn't have is told apart from a call that failed, so the power-mode details card stops
/// asking for it. The LOQ's log showed seven such methods asked every 4 s. Real Windows: the error comes from asking a
/// standard Windows class for a method it doesn't have.
/// </summary>
public class MissingWmiMethodTests
{
    private static ManagementException RealMissingMethodError()
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                try
                {
                    ((ManagementObject)item).GetMethodParameters("IndraNoSuchMethod");
                }
                catch (ManagementException ex)
                {
                    return ex;
                }
            }
        }

        throw new InvalidOperationException("Windows answered for a method that doesn't exist.");
    }

    [Fact]
    public void AMethodWindowsSaysDoesntExist_IsRecognised()
    {
        var missing = RealMissingMethodError();

        Assert.True(PowerModeProfileReader.IsMethodMissing(missing));

        // As Indra's WMI helper passes it on: wrapped in its own exception with the query in the message.
        Assert.True(PowerModeProfileReader.IsMethodMissing(new ManagementException($"Call failed: {missing.Message}", missing)));

        // As a task passes it on when waited for directly.
        Assert.True(PowerModeProfileReader.IsMethodMissing(new AggregateException(missing)));
    }

    [Fact]
    public void OtherFailures_AreNotTakenForAMissingMethod()
    {
        Assert.False(PowerModeProfileReader.IsMethodMissing(new TimeoutException("Windows didn't answer within 60 s.")));
        Assert.False(PowerModeProfileReader.IsMethodMissing(new InvalidOperationException("No results in query")));
        Assert.False(PowerModeProfileReader.IsMethodMissing(null));
    }
}
