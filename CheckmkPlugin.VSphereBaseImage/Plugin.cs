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
/// (Plugin 1) — ohne Plugin 1 im plugins/-Ordner faellt die Tab-Erstellung fehl
/// (Log-Warnung, Cockpit laeuft weiter).</summary>
public sealed class Plugin : IPlugin
{
    public PluginMetadata Metadata => new(
        Id: "kroste.checkmk.vsphere-baseimage",
        Name: "vSphere Baseimages",
        Version: ThisAssembly.Version,
        Description: "Batch-Aktualisierung des Checkmk-Agents in vSphere-CTX-Baseimages.",
        Author: "Kroste",
        UpdateChannelUrl: "https://api.github.com/repos/Kroste/Checkmk-Plugin-VSphereBaseImage/releases/latest");

    public void Register(IServiceCollection services, IPluginContext context)
    {
        var dataDir = context.PluginDataDirectory;

        services.AddSingleton<IBatchSettingsStore>(_ => new BatchSettingsStore(dataDir));
        services.AddSingleton<IVmFilterStore>(_ => new VmFilterStore(dataDir));
        services.AddSingleton<VmFilterCollection>();

        // Windows-only Services (DPAPI + WinRM).
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ICredentialStore>(_ => new DpapiCredentialStore(dataDir));
            services.AddSingleton<IDdcCredentialStore>(_ => new DpapiDdcCredentialStore(dataDir));
            services.AddSingleton<VmRemoteTools>();
            services.AddSingleton<VSphereViewModel>();
            services.AddSingleton<ITabContribution, VSphereTabContribution>();
        }
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
