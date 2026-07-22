using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.UI;

namespace CheckmkPlugin.VSphereBaseImage.Views;

[SupportedOSPlatform("windows")]
public partial class SettingsDialog : ChromeWindow
{
    private readonly ICredentialStore _vSphereStore = null!;
    private readonly IDdcCredentialStore _ddcStore = null!;

    public SettingsDialog(ICredentialStore vSphereStore, IDdcCredentialStore ddcStore)
    {
        AvaloniaXamlLoader.Load(this);
        _vSphereStore = vSphereStore;
        _ddcStore = ddcStore;

        // vCenter-Tab vorbelegen
        var vs = vSphereStore.Load();
        this.FindControl<TextBox>("VCenterUrlBox")!.Text = vs.VCenterUrl;
        this.FindControl<TextBox>("VCenterUserBox")!.Text = vs.UserName;
        this.FindControl<CheckBox>("VCenterIgnoreCertBox")!.IsChecked = vs.IgnoreCertificateErrors;
        // Passwort NIE vorbelegen — leerer Wert = "nicht anfassen" beim Save.

        // DDC-Tab vorbelegen
        var ddc = ddcStore.Load();
        this.FindControl<TextBox>("DdcUrlBox")!.Text = ddc.DdcUrl;
        this.FindControl<TextBox>("DdcUserBox")!.Text = ddc.UserName;
        this.FindControl<CheckBox>("DdcIgnoreCertBox")!.IsChecked = ddc.IgnoreCertificateErrors;
    }

    public SettingsDialog() => AvaloniaXamlLoader.Load(this);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // vCenter
        var vs = _vSphereStore.Load();
        vs.VCenterUrl = (this.FindControl<TextBox>("VCenterUrlBox")!.Text ?? "").Trim();
        vs.UserName = (this.FindControl<TextBox>("VCenterUserBox")!.Text ?? "").Trim();
        vs.IgnoreCertificateErrors = this.FindControl<CheckBox>("VCenterIgnoreCertBox")!.IsChecked ?? false;
        var vsPw = this.FindControl<TextBox>("VCenterPasswordBox")!.Text ?? "";
        _vSphereStore.Save(vs, string.IsNullOrEmpty(vsPw) ? null : vsPw);

        // DDC
        var ddc = _ddcStore.Load();
        ddc.DdcUrl = (this.FindControl<TextBox>("DdcUrlBox")!.Text ?? "").Trim();
        ddc.UserName = (this.FindControl<TextBox>("DdcUserBox")!.Text ?? "").Trim();
        ddc.IgnoreCertificateErrors = this.FindControl<CheckBox>("DdcIgnoreCertBox")!.IsChecked ?? false;
        var ddcPw = this.FindControl<TextBox>("DdcPasswordBox")!.Text ?? "";
        _ddcStore.Save(ddc, string.IsNullOrEmpty(ddcPw) ? null : ddcPw);

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
