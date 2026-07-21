using Avalonia.Controls;

namespace CheckmkPlugin.VSphereBaseImage.UI;

/// <summary>
/// Custom-Chrome nach Avalonia-12-Konvention (Kroste-Standard, dupliziert aus
/// dem Cockpit-Repo weil das Plugin die Kroste-Look-Basisklasse braucht und
/// wir noch kein gemeinsames UI-Package haben). Bei Chrome-Aenderungen im
/// Cockpit hier nachziehen — siehe deps/README.md.
/// </summary>
public class ChromeWindow : Window
{
    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;
        // Kein Icon-Load — das Plugin bringt kein eigenes Icon mit, es erbt
        // im logischen Sinn den Look ueber das Application.Resources (aus dem
        // Cockpit-App.axaml) zur Laufzeit.
    }
}
