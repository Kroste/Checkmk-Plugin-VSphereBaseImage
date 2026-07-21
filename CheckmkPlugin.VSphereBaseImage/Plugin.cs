using System.Runtime.Versioning;
using Checkmk.PluginContracts;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.VSphereBaseImage.Contributions;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CheckmkPlugin.VSphereBaseImage;

/// <summary>vSphere-Baseimage-Plugin. Blendet einen eigenen Tab ein, in dem man
/// VMs aus dem vCenter listen/filtern und im Batch den Checkmk-Agent
/// aktualisieren kann. Nutzt <see cref="IAgentUpdater"/> vom AgentUpdater-Plugin
/// (Plugin 1) — ohne Plugin 1 im plugins/-Ordner faellt die Registrierung des
/// Tab-Contribution fehl (Log-Warnung, Cockpit laeuft weiter).</summary>
public sealed class Plugin : IPlugin
{
    public PluginMetadata Metadata => new(
        Id: "kroste.checkmk.vsphere-baseimage",
        Name: "vSphere Baseimages",
        Version: ThisAssembly.Version,
        Description: "Batch-Aktualisierung des Checkmk-Agents in vSphere-CTX-Baseimages.",
        Author: "Kroste");

    public void Register(IServiceCollection services, IPluginContext context)
    {
        var dataDir = context.PluginDataDirectory;

        // Windows-only Services.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ICredentialStore>(_ => new DpapiCredentialStore(dataDir));
        }

        services.AddSingleton<IBatchSettingsStore>(_ => new BatchSettingsStore(dataDir));
        services.AddSingleton<IVmFilterStore>(_ => new VmFilterStore(dataDir));

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<VSphereViewModel>();
            services.AddSingleton<ITabContribution>(sp =>
                new WindowsGuardTabContribution(sp, context));
        }
    }
}

/// <summary>
/// Wrapper, der den Tab nur unter Windows anmeldet und mit einer aussagekraeftigen
/// Meldung reagiert, wenn der IAgentUpdater (aus Plugin 1) fehlt.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsGuardTabContribution : ITabContribution
{
    private readonly IServiceProvider _sp;
    private readonly IPluginContext _ctx;

    public WindowsGuardTabContribution(IServiceProvider sp, IPluginContext ctx)
    {
        _sp = sp;
        _ctx = ctx;
    }

    public string Header => "vSphere Baseimages";
    public int Order => 1000;

    public object CreateView()
    {
        // IAgentUpdater kommt vom Plugin 1 — ohne das Plugin bleibt der Batch
        // sinnlos. Der Contribution-Konstruktor greift zur Runtime auf den DI zu.
        var updater = _sp.GetService<IAgentUpdater>();
        if (updater is null)
            throw new InvalidOperationException(
                "vSphere-Plugin benoetigt den AgentUpdater (Plugin 1) im plugins/-Ordner.");

        return new VSphereTabContribution(_ctx).CreateView();
    }
}

internal static class ThisAssembly
{
    public static string Version => typeof(Plugin).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion?.Split('+')[0]
        ?? "0.0.0";
}
