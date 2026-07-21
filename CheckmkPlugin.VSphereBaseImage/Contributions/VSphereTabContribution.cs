using System.Runtime.Versioning;
using Checkmk.PluginContracts;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.ViewModels;
using CheckmkPlugin.VSphereBaseImage.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Contributions;

/// <summary>Blendet den "vSphere Baseimages"-Tab neben Status/Hosts/Dashboard ein.</summary>
[SupportedOSPlatform("windows")]
public sealed class VSphereTabContribution : ITabContribution
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _services;

    public VSphereTabContribution(IPluginContext ctx) => _services = ctx.Services;

    public string Header => "vSphere Baseimages";
    public int Order => 1000;

    public object CreateView()
    {
        try
        {
            var vm = _services.GetRequiredService<VSphereViewModel>();
            var credStore = _services.GetRequiredService<ICredentialStore>();
            var batchSettings = _services.GetRequiredService<IBatchSettingsStore>();
            var filterStore = _services.GetRequiredService<IVmFilterStore>();
            return new VSphereView(vm, credStore, batchSettings, filterStore);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "vSphere-Tab-View konnte nicht erstellt werden.");
            throw;
        }
    }
}
