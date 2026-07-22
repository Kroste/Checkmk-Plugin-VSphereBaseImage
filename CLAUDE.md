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
| Citrix-REST-Client | `Services/CitrixClient.cs` — Orchestration-API, `POST tokens` → Bearer, `$UpdateProvisioningScheme` |
| Credential-Stores | `Services/CredentialStore.cs` (vCenter) und `Services/DdcCredentialStore.cs` (Citrix DDC) — beides DPAPI-CurrentUser, **KEINE** Vorbelegung mit `Environment.UserName` (weicht typischerweise ab) |
| Batch-Runner | `Services/BatchRunner.cs` — PowerOn → Tools-Ready → Ping → IAgentUpdater → GuestShutdown → Off → Snapshot → Citrix-Publish → Power-State-Restore |
| Remote-Tools | `Services/VmRemoteTools.cs` — RDP (`mstsc /v:<vm>`) und Ping (`cmd /k ping -t`) fuer das Kontextmenue am VM-Grid |
| VM-Filter | `Models/VmFilter.cs` + `Services/VmFilterCollection.cs` (Live-Wrapper analog Cockpit-HostFilterCollection) |
| UI | `Views/VSphereView.axaml`, `Views/FilterManagerWindow.axaml`, `Views/SettingsDialog.axaml`, `Views/BatchSettingsDialog.axaml`, `Views/CredentialDialog.axaml`, `Views/CatalogPickerDialog.axaml` |
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
  summiert. Timeouts über `BatchSettings` konfigurierbar (Defaults 600 s /
  300 s / 5 s Poll; Untergrenzen 30 s / 1 s erzwingt der Runner).
- **Batch-Settings**: eigene AgentShare + PowerShell-Skript-Vorlage pro Plugin
  (nicht die vom AgentUpdater-Plugin). Zweck: Baseimages können andere
  Voraussetzungen brauchen (z. B. Register mit anderem Site-Namen).
- **UpdateChannelUrl** in `PluginMetadata` gesetzt — Cockpit-Plugin-Update
  (ab Cockpit v1.7.2) prüft und installiert automatisch.
- **Kontextmenü am VM-Grid** *(v0.6.0)*: RDP öffnen (`mstsc /v:<vm-name>`),
  Ping-Fenster (`cmd /k ping -t`), VM-Name/VM-ID in die Zwischenablage.
  Ab v0.7.0 öffnet der Doppelklick das **VM-Detail-Fenster** (siehe unten)
  statt direkt RDP; RDP bleibt im Kontextmenü und im Detail-Header.
- **VM-Detail-Fenster** *(v0.7.0, `VmDetailWindow`/`VmDetailViewModel`)*
  analog Cockpit-`HostDetailWindow`: Header mit VM-Metadaten + RDP/Ping/
  Aktualisieren; Guest-Panel (Hostname, IP, Guest-OS, VMware-Tools-RunState —
  nur wenn VM läuft); Snapshot-Liste mit Delete-Button pro Zeile. Das Fenster
  öffnet on-demand einen eigenen `VSphereClient` und ist damit unabhängig
  vom Batch-Client im VM.
- **Batch-Abbruch** *(v0.7.0)*: während eines laufenden Batches erscheint
  neben „Batch starten" ein rotes „Batch abbrechen". Löst die CTS aus,
  Warteschritte (Tools-Ready, Ping, Power-Off) brechen sauber ab, die
  aktuell laufende VM wird zu Ende geführt.
- **Snapshot-Retention** *(v0.7.0)*: nach dem Anlegen eines neuen
  `checkmk-update-*`-Snapshots schneidet der `BatchRunner` alte Snapshots
  mit demselben Präfix auf N=3 zurück (Präfix + Anzahl als Konstanten in
  `BatchRunner.SnapshotPrefix`/`SnapshotRetentionCount`). Manuelle
  Snapshots ohne Präfix bleiben unberührt. Delete-Fehler sind non-fatal.
- **QoL-Tastenkürzel** *(v0.9.0)*: F5 aktualisiert; Enter im Freitext-
  Filter selektiert die erste gefilterte VM und übergibt den Fokus ans
  Grid. Ctrl+F fokussiert weiterhin den Filter, Esc leert ihn.
- **Log-Panel-Kontextmenü** *(v0.9.0)*: Rechtsklick bietet Log kopieren,
  Log als Datei speichern (SaveFilePicker mit Default-Namen
  `vsphere-batch-yyyyMMdd-HHmm.log`) und Log leeren.
- **Batch-Bericht-Modal** *(v0.9.0, `BatchReportDialog`/-VM)*: nach
  Batch-Ende (auch nach Abbruch) erscheint ein Zusammenfassungs-Dialog
  mit Aggregat und einer VM-Ergebnis-Tabelle. „Bericht kopieren" legt
  eine druckbare Textform in die Zwischenablage; „Log speichern…" ist
  auch aus dem Modal erreichbar. Der Runner meldet Cancels inzwischen
  als reguläre Result-Zeile statt Exception, damit der Report auch
  Cancel-Fälle abbildet.

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
   drin). ✅ **Snapshot-Retention** *(v0.7.0)*: nach der Anlage
   schneidet der Runner alte `checkmk-update-*`-Snapshots derselben VM
   auf N=3 zurück (`BatchRunner.SnapshotRetentionCount`), manuelle
   Snapshots ohne Präfix bleiben unangetastet.

   c. ✅ **Citrix-CVAD-On-Prem-Machine-Catalog-Update** *(v0.5.0)* — nach
   dem Snapshot hebt `CitrixClient.PublishMasterImageAsync` das Master-Image
   des zugeordneten Machine Catalogs auf den neuen Snapshot
   (`POST Sites/{sid}/MachineCatalogs/{id}/$UpdateProvisioningScheme`).
   Auth: `POST tokens` mit Basic (DDC-Domänen-User) → Bearer. Site-ID kommt
   aus `GET Sites` (erste Site gewinnt, cached). Der neue Master-Image-Pfad
   wird aus dem aktuellen Katalog-Pfad abgeleitet (`XdHyp:\HostingUnits\{unit}\{vm}.vm\{snap}.snapshot`,
   nur VM- und Snapshot-Anteil ersetzt) — **HostingUnit muss nicht separat
   konfiguriert werden**. VM→Katalog-Zuordnung: manueller `CatalogPickerDialog`
   vor Batch-Start, pro VM ComboBox mit **automatischem Vorschlag** über
   längste gemeinsame Namens-Präfix-Match (min. 3 Zeichen). Trigger-Zeitpunkt:
   pro VM direkt nach Snapshot. Publish-Fehler sind non-fatal (Update +
   Snapshot sind ja schon durch — Admin kann im DDC manuell nachziehen).

2. ✅ **Timeouts konfigurierbar** *(v0.8.0)* — `BatchSettings` hat
   `PowerOnTimeoutSeconds`, `ShutdownTimeoutSeconds`,
   `ToolsPollIntervalSeconds` (Defaults 600/300/5), im
   `BatchSettingsDialog` als Timeout-Panel editierbar. Untergrenzen
   (Timeouts ≥ 30 s, Poll ≥ 1 s) erzwingt der Runner via `Math.Max` — so
   kippt ein zu strammer Wert nicht in einen Sofort-Timeout.

3. ✅ **GUI-Progress-Cancel** *(v0.7.0)* — „Batch abbrechen"-Button
   im Batch-Panel triggert die interne CTS; der Runner unterbricht die
   Warteschritte sauber, die aktuelle VM wird zu Ende geführt, weitere
   VMs werden übersprungen. Externes `CancellationToken` wird via
   `CreateLinkedTokenSource` mit eingekoppelt.

4. **Parallele Batches** — heute streng sequenziell. Für sehr viele Baseimages
   könnte parallel N=3 helfen, aber der Ping/Reachability-Storm und die
   PowerOn-Belastung auf dem Host müssen im Auge behalten werden. Erst
   messen ob überhaupt nötig.

## 5 · Deal

Lars entwirft, Claude implementiert. Änderungen als Commit/Patch — die
Contracts-DLL im `deps/`-Ordner ist nur bei Contracts-Änderungen zu aktualisieren
(dann aus dem Cockpit-Release-Build kopieren und mit eigenem Commit reinstellen).
