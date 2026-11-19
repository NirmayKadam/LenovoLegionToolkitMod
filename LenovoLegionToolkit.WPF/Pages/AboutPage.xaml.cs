using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class AboutPage
{
    private static string VersionText
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version is null)
                return string.Empty;
            if (version.IsBeta())
                return "BETA";
            return version.ToString();
        }
    }

    private static string BuildText => Assembly.GetEntryAssembly()?.GetBuildDateTimeString() ?? string.Empty;

    /// <summary>The GPL-3.0 text the installer copies next to the app; read online when it isn't there (portable copies).</summary>
    private static readonly Uri OnlineLicence = new("https://github.com/NirmayKadam/LenovoLegionToolkitMod/blob/main/LICENSE");

    public AboutPage()
    {
        InitializeComponent();

        _version.Text += $" {VersionText}";
        _build.Text += $" {BuildText}";
        _latestReleaseLink.Tag = UpdateChecker.LatestReleasePage.AbsoluteUri;
    }

    private void ViewLicence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var licence = Path.Combine(Folders.Program, "LICENSE");
            if (File.Exists(licence))
                Process.Start("notepad.exe", $"\"{licence}\"");
            else
                Process.Start("explorer.exe", OnlineLicence.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to open the licence.", ex);
        }

        e.Handled = true;
    }

    private void OpenApplicationDataFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(Folders.Settings))
            return;

        Process.Start("explorer", Folders.Settings);
    }

    private void OpenApplicationTempFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(Folders.Temp))
            return;

        Process.Start("explorer", Folders.Temp);
    }
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        string logPath = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\LenovoLegionToolkit\\log");

        if (Directory.Exists(logPath))
        {
            Process.Start("explorer.exe", logPath);
        }
    }
 
    private void OpenSafetyReport_Click(object sender, RoutedEventArgs e) =>
        new LenovoLegionToolkit.WPF.Windows.Settings.SafetyReportWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is Hyperlink { Tag: string uriString })
        {
            Process.Start("explorer.exe", uriString);
            e.Handled = true;
        }
    }
}
