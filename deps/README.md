# Vendored Plugin-Contracts

Die `Checkmk.PluginContracts.dll` liegt hier als committed Binary, weil das
Cockpit-Team (noch) keinen `write:packages`-Scope auf GitHub Packages hat.
Bei Contracts-Updates: neue DLL aus dem Cockpit-Repo
(`Checkmk.PluginContracts/bin/Release/net10.0/`) hier drüber kopieren und mit
einem eigenen Commit reinstellen.

Wenn wir irgendwann GitHub Packages ordentlich aufsetzen, wandert das auf eine
`PackageReference` und dieser Ordner verschwindet.
