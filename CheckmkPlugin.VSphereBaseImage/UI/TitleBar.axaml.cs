using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CheckmkPlugin.VSphereBaseImage.UI;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public TitleBar()
    {
        InitializeComponent();

        MinButton.Click += (_, _) => { if (Host is { } w) w.WindowState = WindowState.Minimized; };
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Host?.Close();

        Bar.PointerPressed += OnBarPointerPressed;
        Bar.DoubleTapped += (_, _) => ToggleMaximize();
    }

    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void ToggleMaximize()
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
