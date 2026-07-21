# Checkmk-Plugin-VSphereBaseImage

Plugin für das [Checkmk Cockpit](https://github.com/Kroste/Checkmk), das die
**automatische Aktualisierung des Checkmk-Agents in vSphere-CTX-Baseimages**
übernimmt.

## Problem

Im Fachbereich werden VMs wie `CTX??..??00` (Baseimages) in der Nacht per
vSphere neu ausgerollt — die abgeleiteten `01`-VMs bekommen einen frischen
Zustand aus dem Baseimage. Updates, die tagsüber im `01` durchgeführt werden,
gehen dabei verloren. Der Checkmk-Agent muss also **im Baseimage selbst**
aktualisiert werden, und das Baseimage ist normalerweise **ausgeschaltet**.

Manuell heißt das: pro Baseimage einloggen ins vSphere, Power-On, warten,
RDP/Remote-Update, Shutdown — 5 Minuten Klickerei pro VM, mal 20+ Baseimages.

## Lösung

Plugin blendet einen eigenen Tab **„vSphere Baseimages"** im Cockpit ein:

1. Verbindet sich per REST-API mit dem vCenter (v7+, Session-Auth).
2. Listet alle VMs mit Filter (Regex oder Include-Liste).
3. Batch-Button startet für jede sichtbare VM sequenziell:
   - Power-On
   - Warten bis VMware-Tools „Running"
   - Warten bis Guest per Ping erreichbar
   - Agent-Update via `IAgentUpdater` (aus dem AgentUpdater-Plugin)
   - Guest-Shutdown
   - Warten bis Power-Off
   - → nächste VM
4. Fehler bei einer VM brechen die Batch nicht ab.

## Voraussetzungen

- Windows (DPAPI, Ping, Remote-PowerShell via Plugin 1).
- Checkmk Cockpit **≥ v1.7.0** (Plugin-System).
- [`Checkmk-Plugin-AgentUpdater`](https://github.com/Kroste/Checkmk-Plugin-AgentUpdater)
  **im gleichen `plugins/`-Ordner** — Plugin 2 nutzt dessen `IAgentUpdater`
  für das eigentliche Update. Ohne Plugin 1: Tab lädt nicht (Log-Meldung).
- vCenter-Konto mit Berechtigungen für VM-Power-Ops und Guest-Ops.
- Admin-Konto auf den Baseimage-VMs mit WinRM-Rechten.

## Installation

1. ZIP des neuesten [Releases](https://github.com/Kroste/Checkmk-Plugin-VSphereBaseImage/releases)
   herunterladen und entpacken.
2. `CheckmkPlugin.VSphereBaseImage.dll` in **`plugins/`** neben deiner
   `Checkmk.App.exe` legen (Ordner ggf. anlegen).
3. Cockpit starten. Neuer Tab „vSphere Baseimages" erscheint rechts.

## Einrichtung

**vCenter-Einstellungen** (Button im Tab):

- **URL**: z. B. `https://vc.lhp.intern`
- **Anmeldename**: dein vCenter-Konto (`user@vsphere.local` oder `DOMAENE\User`).
  Weicht typischerweise vom Windows-Login ab — deshalb keine Vorbelegung.
- **Passwort**: DPAPI-verschlüsselt user-lokal gespeichert.
- **Zertifikatsfehler ignorieren**: nur bei self-signed vCenter-Cert.

**Batch-Skript & Agent-Share** (Button im Tab): Analog zum AgentUpdater-Plugin —
Share mit dem aktuellen MSI, PowerShell-Skript-Vorlage. Wird auf **jeder**
Baseimage-VM während des Batch ausgeführt.

**VM-Filter** verwalten: Klick auf „Filter verwalten…" öffnet die `filter.json`
im OS-Editor (ein GUI-Editor kommt in einer späteren Version). Struktur:

```json
{
  "Filters": [
    {
      "Name": "Alle Baseimages",
      "NameRegex": "^CTX\\d+.*00$",
      "IncludeNames": []
    }
  ],
  "ActiveFilterName": "Alle Baseimages"
}
```

## Ablauf beim Batch-Start

1. Klick auf **„Batch starten (N VMs im Filter …)"**.
2. **Admin-Anmeldung-Dialog**: der Nutzer für die Remote-PowerShell-Sessions
   auf den Baseimage-Guest-VMs. Nur im Prozess-Memory, nicht gespeichert.
3. Fortschritt im Log-Panel unten: pro VM detailliert (Power-On → Tools →
   Ping → Update → Shutdown).
4. Ergebnis-Summe: „Batch beendet: 18/20 erfolgreich."
5. Fehler stehen im Log — meist Timeout beim Tools-Start oder WinRM-Problem.

## Wo liegen meine Daten

| Was | Wo |
|---|---|
| vCenter-URL/User (Passwort DPAPI-verschlüsselt) | `%APPDATA%\Kroste\Checkmk\plugins\kroste.checkmk.vsphere-baseimage\credentials.json` |
| Agent-Share + Skript | `.../batch-settings.json` |
| VM-Filter | `.../filter.json` |

## Sicherheit

- vCenter-Passwort: DPAPI (CurrentUser), user-lokal — nur du kannst es
  entschlüsseln, nicht andere User am gleichen Rechner.
- Admin-Passwort für Remote-PowerShell: NICHT persistent, nur pro Batch-Lauf im
  Memory.
- Skript-Vorlage/Passwörter werden nie ins Log geschrieben.

## Fachbereich

5424 IT-Basis-Dienste (LHP)
