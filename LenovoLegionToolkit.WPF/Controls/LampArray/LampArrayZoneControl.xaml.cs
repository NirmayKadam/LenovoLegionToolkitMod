using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Controls.LampArray;

public partial class LampArrayZoneControl : UserControl
{
    public static readonly DependencyProperty KeyCodeProperty = DependencyProperty.Register(
        nameof(KeyCode), typeof(ushort), typeof(LampArrayZoneControl), new PropertyMetadata(default(ushort)));

    public ushort KeyCode
    {
        get => (ushort)GetValue(KeyCodeProperty);
        set => SetValue(KeyCodeProperty, value);
    }

    public static readonly DependencyProperty IndicesProperty = DependencyProperty.Register(
        nameof(Indices), typeof(string), typeof(LampArrayZoneControl), new PropertyMetadata(default(string), OnIndicesChanged));

    private static void OnIndicesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LampArrayZoneControl control)
        {
            control.ParseIndices((string)e.NewValue);
        }
    }
    
    public string Indices
    {
        get => (string)GetValue(IndicesProperty);
        set => SetValue(IndicesProperty, value);
    }

    public static readonly DependencyProperty ShowBacklightProperty = DependencyProperty.Register(
        nameof(ShowBacklight), typeof(bool), typeof(LampArrayZoneControl), new PropertyMetadata(true));

    public bool ShowBacklight
    {
        get => (bool)GetValue(ShowBacklightProperty);
        set => SetValue(ShowBacklightProperty, value);
    }
    
    public static readonly DependencyProperty LampBrushProperty = DependencyProperty.Register(
        nameof(LampBrush), typeof(Brush), typeof(LampArrayZoneControl), new PropertyMetadata(null));

    public Brush? LampBrush
    {
        get => (Brush?)GetValue(LampBrushProperty);
        set => SetValue(LampBrushProperty, value);
    }
    
    public static readonly DependencyProperty ButtonBackgroundProperty = DependencyProperty.Register(
        nameof(ButtonBackground), typeof(Brush), typeof(LampArrayZoneControl), new PropertyMetadata(Brushes.Transparent));

    public Brush ButtonBackground
    {
        get => (Brush)GetValue(ButtonBackgroundProperty);
        set => SetValue(ButtonBackgroundProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(LampArrayZoneControl), new PropertyMetadata(null));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
    
    public Color? Color
    {
        get => (LampBrush as SolidColorBrush)?.Color;
        set => LampBrush = value.HasValue ? new SolidColorBrush(value.Value) : null;
    }
    
    private int[] _parsedIndices = Array.Empty<int>();
    
    public int[] GetIndices()
    {
        if (_parsedIndices.Length > 0) return _parsedIndices;
        return new int[] { KeyCode };
    } 

    private void ParseIndices(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _parsedIndices = Array.Empty<int>();
            return;
        }

        var list = new System.Collections.Generic.List<int>();
        var parts = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var trim = part.Trim();
            if (trim.Contains("-"))
            {
                var range = trim.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                {
                    if (start <= end)
                    {
                        for (var i = start; i <= end; i++) list.Add(i);
                    }
                }
            }
            else
            {
                if (int.TryParse(trim, out var val)) list.Add(val);
            }
        }
        _parsedIndices = list.ToArray();
    }
    
    public bool? IsChecked
    {
        get => _button.IsChecked;
        set => _button.IsChecked = value;
    }

    private RoutedEventHandler? _clickHandlers;
    public event RoutedEventHandler Click
    {
        add => _clickHandlers += value;
        remove => _clickHandlers -= value;
    }

    public LampArrayZoneControl()
    {
        InitializeComponent();
        _button.Click += (s, e) => _clickHandlers?.Invoke(this, e);
    }
}
