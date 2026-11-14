using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Controls;

public class LoadableControl : UserControl
{
    private readonly ContentPresenter _contentPresenter = new();

    private readonly ProgressRing _progressRing = new()
    {
        IsIndeterminate = true,
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Center,
        Width = 48,
        Height = 48,
    };

    private bool _isLoading = true;

    /// <summary>The caller's choice: spinning (true) or showing <see cref="Progress"/> (false) while loading.</summary>
    private bool _indeterminate = true;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            UpdateLoadingState();
        }
    }

    public bool IsIndeterminate
    {
        get => _indeterminate;
        set
        {
            _indeterminate = value;
            UpdateLoadingState();
        }
    }

    public double Progress
    {
        get => _progressRing.Progress;
        set => _progressRing.Progress = value;
    }

    public double IndicatorWidth
    {
        get => _progressRing.Width;
        set => _progressRing.Width = value;
    }

    public double IndicatorHeight
    {
        get => _progressRing.Height;
        set => _progressRing.Height = value;
    }

    public HorizontalAlignment IndicatorHorizontalAlignment
    {
        get => _progressRing.HorizontalAlignment;
        set => _progressRing.HorizontalAlignment = value;
    }

    public VerticalAlignment IndicatorVerticalAlignment
    {
        get => _progressRing.VerticalAlignment;
        set => _progressRing.VerticalAlignment = value;
    }

    public Thickness IndicatorMargin
    {
        get => _progressRing.Margin;
        set => _progressRing.Margin = value;
    }

    public Visibility ContentVisibilityWhileLoading { get; set; } = Visibility.Hidden;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        _contentPresenter.Content = Content;

        _progressRing.RenderTransformOrigin = new(0.5, 0.5);
        _progressRing.RenderTransform = new TransformGroup
        {
            Children =
            {
                new RotateTransform(-90),
                new ScaleTransform(-1, 1),
            }
        };

        var grid = new Grid();
        grid.Children.Add(_contentPresenter);
        grid.Children.Add(_progressRing);

        UpdateLoadingState();

        Content = grid;
    }

    /// <summary>
    /// WPF keeps an endless animation running even when its element is hidden (and even when the window is in the
    /// tray), so the ring only spins while loading; otherwise it is stopped and taken out of the layout.
    /// </summary>
    private void UpdateLoadingState()
    {
        _contentPresenter.Visibility = IsLoading ? ContentVisibilityWhileLoading : Visibility.Visible;
        _progressRing.IsIndeterminate = IsLoading && _indeterminate;
        _progressRing.Visibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }
}
