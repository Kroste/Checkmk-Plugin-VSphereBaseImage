using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Models;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.UI;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

[SupportedOSPlatform("windows")]
public partial class VmDetailWindow : ChromeWindow
{
    private readonly VmDetailViewModel? _vm;
    private readonly VmRemoteTools? _remote;

    public VmDetailWindow(VmDetailViewModel vm, VmRemoteTools remote)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;
        _remote = remote;
        DataContext = vm;

        Opened += async (_, _) =>
        {
            if (_vm is not null) await _vm.RefreshCommand.ExecuteAsync(null);
        };
    }

    public VmDetailWindow() => AvaloniaXamlLoader.Load(this);

    private void OnRdpClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Vm.Name is { Length: > 0 } n) _remote?.StartRdp(n);
    }

    private void OnPingClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Vm.Name is { Length: > 0 } n) _remote?.StartPing(n);
    }

    private async void OnDeleteSnapshotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VmSnapshotInfo snap }) return;
        if (_vm is null) return;
        await _vm.DeleteSnapshotCommand.ExecuteAsync(snap);
    }
}
