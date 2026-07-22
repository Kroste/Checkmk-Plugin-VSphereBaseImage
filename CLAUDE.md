# CLAUDE.md — Checkmk-Plugin-VSphereBaseImage

Plugin fürs [Checkmk Cockpit](https://github.com/Kroste/Checkmk), das den
Checkmk-Agent in vSphere-Baseimages im Batch aktualisiert. Diese Datei ist der
projekt-spezifische Kontext für Copilot/Claude — projektübergreifende Standards
kommen aus dem `kroste-avalonia`-Skill.

---

## 1 · Build, Test, Run

```bash
dotnet build Checkmk-Plugin-VSphereBaseImage.slnx -c Release
```

Die entstehende `CheckmkPlugin.VSphereBaseImage.dll` gehört ins `plugins/`-
Verzeichnis neben `Checkmk.App.exe`. Beim Cockpit-Debug-Build (Repo `Checkmk`,
Ordner `external-plugins/vsphere-baseimage/` als Git-Submodule) macht das
MSBuild-Target automatisch die Kopie in `bin/…/plugins/`.

Contracts-Referenz liegt als committed Binary in `deps/Checkmk.PluginContracts.dll`
(siehe `deps/README.md` — bewusst kein GitHub-Packages-Feed, solange der
Kroste-Account keinen `write:packages`-Scope hat).

## 2 · Architektur

| Bereich | Datei |
|---|---|
| Entry-Point (IPlugin) | `Plugin.cs` |
| Contribution | `Contributions/VSphereTabContribution.cs` (ITabContribution, Order 1000) |
| vCenter-REST-Client | `Services/VSphereClient.cs` — Session-Auth mit Basic → `vmware-api-session-id`-Header, auto-relogin bei 401 |
| Credential-Store | `Services/CredentialStore.cs` — DPAPI-CurrentUser, **KEINE** Vorbelegung mit `Environment.UserName` |
| Batch-Runner | `Services/BatchRunner.cs` — PowerOn → Tools-Ready → Ping → IAgentUpdater → GuestShutdown → Off |
| VM-Filter | `Models/VmFilter.cs` + `Services/VmFilterCollection.cs` (Live-Wrapper analog Cockpit-HostFilterCollection) |
| UI | `Views/VSphereView.axaml`, `Views/FilterManagerWindow.axaml`, `Views/SettingsDialog.axaml`, `Views/BatchSettingsDialog.axaml`, `Views/CredentialDialog.axaml` |
| ChromeWindow + TitleBar | `UI/ChromeWindow.cs`, `UI/TitleBar.axaml` — dupliziert aus dem Cockpit (bei Kroste-Chrome-Änderungen hier nachziehen) |

**Plugin-Abhängigkeit**: Plugin 2 konsumiert `IAgentUpdater` aus
`Checkmk-Plugin-AgentUpdater` (Plugin 1). Ohne Plugin 1 im gleichen
`plugins/`-Ordner wirft die DI-Auflösung — MainWindow logged eine Warnung, der
Tab erscheint nicht.

## 3 · Aktueller Funktionsstand

- **vCenter-Verbindung**: URL/User/Passwort im `SettingsDialog`, Passwort
  DPAPI-verschlüsselt. Zertifikatsfehler ignorieren als Option für Lab-Cert.
- **VM-Liste**: `GET /api/vcenter/vm` → Grid mit Name/Power/CPU/RAM/ID.
- **Filter**: Regex ODER Include-Liste (`VmFilter`), verwaltbar im
  `FilterManagerWindow` (analog Cockpit-Filter). Regex-Validierung vor Save.
- **Freitext-Verfeinerung**: TextBox oberhalb des Grids, case-insensitive
  Contains über Name+Power-State. Ctrl+F fokussiert, Esc leert.
- **Batch-Workflow**: für jede sichtbare VM sequenziell PowerOn → Tools-Ready
  → Ping-Reachable → `IAgentUpdater.UpdateAsync` → GuestShutdown → PowerOff.
  Fehler brechen den Batch nicht ab, sondern werden gesammelt und am Ende
  summiert. Timeouts 10 min / 5 min hart codiert.
- **Batch-Settings**: eigene AgentShare + PowerShell-Skript-Vorlage pro Plugin
  (nicht die vom AgentUpdater-Plugin). Zweck: Baseimages können andere
  Voraussetzungen brauchen (z. B. Register mit anderem Site-Namen).
- **UpdateChannelUrl** in `PluginMetadata` gesetzt — Cockpit-Plugin-Update
  (ab Cockpit v1.7.2) prüft und installiert automatisch.

## 4 · Roadmap

1. **Post-Update-Workflow-Erweiterung** (ganze Kette, Reihenfolge:
   Power-State-Restore → Snapshot → Citrix-Katalog). Der Endzustand jeder VM
   soll dem Zustand vor dem Batch entsprechen:

   a. ✅ **Power-State-Restore** *(v0.4.0)* — `BatchRunner` erfasst
   `wasOn = current.IsPoweredOn` am Anfang und stellt den Zustand nach dem
   Snapshot wieder her: VM vorher AN → wieder AN, VM vorher AUS → bleibt AUS.
   Für CTX???00-Baseimages (typisch aus → aus) unverändert; für andere VMs
   korrekt.

   b. ✅ **vSphere-Snapshot nach Agent-Update** *(v0.4.0)* — nach dem
   Shutdown legt `BatchRunner` einen Snapshot an
   (`POST /api/vcenter/vm/{vm}/snapshots`, `memory=false`, Name-Konvention
   `checkmk-update-YYYYMMDD-HHmmss`). Ergebnis (Snapshot-Name + -ID) landet
   im `BatchStepResult`, damit der spätere Citrix-Katalog-Publish darauf
   zugreifen kann. Snapshot-Fehler sind non-fatal (Update ist ja schon
   drin). Snapshot-Retention (alte Snapshots aufräumen) ist noch nicht
   drin — kommt bei Bedarf.

   c. **Citrix-CVAD-On-Prem-Machine-Catalog-Update** — nach dem Snapshot den
   DDC anweisen, das Master-Image des betroffenen Machine Catalogs auf den
   neuen Snapshot zu heben. **Auth-Weg entschieden: Citrix-Orchestration-
   REST-API** (`https://<ddc>/citrix/orchestration/api/…`, `POST /tokens` für
   Bearer, dann `POST /{siteid}/MachineCatalogs/{catalog}/$UpdateProvisioningScheme`
   mit `MasterImage: XdHyp:\\HostingUnits\\<unit>\\<vmname>.vm\\<snapshot>.snapshot`).
   DDC-Anmeldedaten sind seit v0.3.0 im Settings-Dialog konfigurierbar
   (`ddc-credentials.json`, DPAPI). **Noch zu klären vor Umsetzung**:

   - **VM → Katalog-Zuordnung**: Namenskonvention (`CTX01100` → Katalog
     `CTX01`, Regex-Capture in Batch-Settings), manuelle Auswahl je VM per
     Dropdown aus `GET /MachineCatalogs`, oder am `VmFilter` mitspeichern.
   - **Trigger-Zeitpunkt**: pro VM direkt nach Snapshot, oder Sammel-Publish
     am Batch-Ende (einmal je Katalog).
   - **Site-ID im DDC**: single-Site oder wählbar in den Settings?
   - **Hosting-Unit-Name** für den XdHyp-Pfad (kommt vermutlich aus dem
     bereits konfigurierten vSphere-Hosting im DDC, per `GET /Hypervisors`
     abfragbar).

2. **Timeouts konfigurierbar machen** — aktuell PowerOn 10 min, Shutdown
   5 min, Tools-Poll 5 s hart im Code. In `BatchSettings` verschieben und
   im `BatchSettingsDialog` bearbeitbar machen.

3. **GUI-Progress-Cancel** — der Batch läuft asynchron, ist aber nicht
   abbrechbar. Cancel-Button im Log-Panel, der die interne
   `CancellationToken` triggert.

4. **Parallele Batches** — heute streng sequenziell. Für sehr viele Baseimages
   könnte parallel N=3 helfen, aber der Ping/Reachability-Storm und die
   PowerOn-Belastung auf dem Host müssen im Auge behalten werden. Erst
   messen ob überhaupt nötig.

## 5 · Deal

Lars entwirft, Claude implementiert. Änderungen als Commit/Patch — die
Contracts-DLL im `deps/`-Ordner ist nur bei Contracts-Änderungen zu aktualisieren
(dann aus dem Cockpit-Release-Build kopieren und mit eigenem Commit reinstellen).
