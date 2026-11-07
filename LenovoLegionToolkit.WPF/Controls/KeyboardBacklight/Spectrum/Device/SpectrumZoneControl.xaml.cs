using System.Windows;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device;

public partial class SpectrumZoneControl
{
    private ushort _keyCode;

    public ushort KeyCode
    {
        get => _keyCode;
        set
        {
            _keyCode = value;

#if DEBUG
            _button.ToolTip = $"0x{value:X2}";
#endif
        }
    }

    public double RotationAngle
    {
        get => _rotateTransform.Angle;
        set => _rotateTransform.Angle = value;
    }

    public Color? Color
    {
        get => (_background.Background as SolidColorBrush)?.Color;
        set
        {
            if (!value.HasValue)
                _background.Background = null;
            else if (_background.Background is SolidColorBrush brush)
                brush.Color = value.Value;
            else
                _background.Background = new SolidColorBrush(value.Value);
        }
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

    public SpectrumZoneControl()
    {
        InitializeComponent();
        _button.Click += (s, e) => _clickHandlers?.Invoke(this, e);
    }
}
