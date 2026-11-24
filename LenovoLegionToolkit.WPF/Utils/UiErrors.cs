using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// What a button or switch does when its action fails. Without this, the error reached the app's general
/// "Unexpected exception" message, and the control could be left greyed out or showing a state that wasn't applied.
/// </summary>
public static class UiErrors
{
    /// <summary>Logs the failure of <paramref name="what"/> and shows its message as a warning. Never throws.</summary>
    public static void Report(string what, Exception ex)
    {
        Log.Instance.Trace($"{what} failed.", ex);
        try
        {
            SnackbarHelper.Show(Resource.Warning, ex.Message, SnackbarType.Error);
        }
        catch (Exception showError)
        {
            Log.Instance.Trace($"Couldn't show that {what} failed.", showError);
        }
    }

    /// <summary>Runs a user action, reporting a failure (a cancellation isn't one). Never throws.</summary>
    public static async Task RunAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose (the window closed).
        }
        catch (Exception ex)
        {
            Report(what, ex);
        }
    }
}
