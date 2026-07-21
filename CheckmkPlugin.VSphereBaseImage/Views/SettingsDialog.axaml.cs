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
    private readonly ICredentialStore _store = null!;

    public SettingsDialog(ICredentialStore store)
    {
        AvaloniaXamlLoader.Load(this);
        _store = store;

        var creds = store.Load();
        this.FindControl<TextBox>("UrlBox")!.Text = creds.VCenterUrl;
        this.FindControl<TextBox>("UserBox")!.Text = creds.UserName;
        this.FindControl<CheckBox>("IgnoreCertBox")!.IsChecked = creds.IgnoreCertificateErrors;
        // Passwort NIE vorbelegen — leerer Wert = "nicht anfassen" beim Save.
    }

    public SettingsDialog() => AvaloniaXamlLoader.Load(this);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var creds = _store.Load();
        creds.VCenterUrl = (this.FindControl<TextBox>("UrlBox")!.Text ?? "").Trim();
        creds.UserName = (this.FindControl<TextBox>("UserBox")!.Text ?? "").Trim();
        creds.IgnoreCertificateErrors = this.FindControl<CheckBox>("IgnoreCertBox")!.IsChecked ?? false;

        var pw = this.FindControl<TextBox>("PasswordBox")!.Text ?? "";
        // Leer -> nicht anfassen (null durchreichen).
        _store.Save(creds, string.IsNullOrEmpty(pw) ? null : pw);
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
