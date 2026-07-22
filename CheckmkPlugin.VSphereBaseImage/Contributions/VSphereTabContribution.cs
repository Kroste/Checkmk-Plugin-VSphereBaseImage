using System.Runtime.Versioning;
using Checkmk.PluginContracts;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.ViewModels;
using CheckmkPlugin.VSphereBaseImage.Views;

namespace CheckmkPlugin.VSphereBaseImage.Contributions;

/// <summary>
/// Blendet den "vSphere Baseimages"-Tab neben Status/Hosts/Dashboard ein.
/// Ctor-Dependencies werden vom DI-Container direkt aufgeloest — kein Umweg
/// ueber <see cref="IPluginContext"/> (der ist nur ein Register-Callback-Argument,
/// nicht im DI registriert und wuerde die Aufloesung crashen).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VSphereTabContribution : ITabContribution
{
    private readonly VSphereViewModel _vm;
    private readonly ICredentialStore _credStore;
    private readonly IDdcCredentialStore _ddcStore;
    private readonly IBatchSettingsStore _batchSettings;
    private readonly VmFilterCollection _filters;

    public VSphereTabContribution(
        VSphereViewModel vm,
        ICredentialStore credStore,
        IDdcCredentialStore ddcStore,
        IBatchSettingsStore batchSettings,
        VmFilterCollection filters)
    {
        _vm = vm;
        _credStore = credStore;
        _ddcStore = ddcStore;
        _batchSettings = batchSettings;
        _filters = filters;
    }

    public string Header => "vSphere Baseimages";
    public int Order => 1000;

    public object CreateView() => new VSphereView(_vm, _credStore, _ddcStore, _batchSettings, _filters);
}
