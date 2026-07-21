using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.UI;

namespace CheckmkPlugin.VSphereBaseImage.Views;

public sealed record CredentialResult(string User, string Password);

public partial class CredentialDialog : ChromeWindow
{
    public CredentialDialog(string prompt)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;
    }

    public CredentialDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var user = (this.FindControl<TextBox>("UserBox")!.Text ?? "").Trim();
        var pass = this.FindControl<TextBox>("PassBox")!.Text ?? "";
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return;
        Close(new CredentialResult(user, pass));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
