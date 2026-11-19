using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using Markdig;
using Markdig.Wpf;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

/// <summary>
/// Shows the release notes of newer releases of this fork and opens the release page. Nothing is downloaded or
/// installed from here.
/// </summary>
public partial class UpdateWindow
{
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();

    private Update[] _updates = [];

    public UpdateWindow()
    {
        InitializeComponent();
        _markdownViewer.Pipeline = new MarkdownPipelineBuilder().UseSupportedExtensions().Build();
    }

    private void Hyperlink_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            uri.Open();
            e.Handled = true;
        }
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _updates = await _updateChecker.GetUpdatesAsync();

            var stringBuilder = new StringBuilder();
            foreach (var update in _updates)
            {
                stringBuilder.AppendLine("**" + update.Title + "**   _(" + update.Date.ToString("D") + ")_")
                    .AppendLine()
                    .AppendLine(update.Description)
                    .AppendLine();
            }

            _markdownViewer.Markdown = stringBuilder.ToString();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to show the release notes.", ex);
        }
        finally
        {
            _openReleaseButton.IsEnabled = true;
        }
    }

    private void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        var newest = _updates.OrderByDescending(u => u.Version).Select(u => u.ReleasePage).FirstOrDefault();
        UpdateChecker.ReleasePageOrDefault(newest).Open();
        Close();
    }
}
