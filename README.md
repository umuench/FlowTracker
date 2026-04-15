# FlowTracker

FlowTracker ist eine lokale Windows-Desktop-Anwendung auf Basis von .NET 10 und WPF.
Die App startet tray-only im Hintergrund und speichert Daten ausschließlich lokal (SQLite), ohne Netzwerkversand.

## Technologie-Stack

- .NET 10 (`net10.0-windows`)
- WPF
- C# (Preview, modernes Sprachlevel)
- SQLite (`Microsoft.Data.Sqlite`)
- Dapper
- Tray-Integration mit `H.NotifyIcon.Wpf`

## Projektstruktur

- `FlowTracker.slnx` - XML-basierte Solution (neues Format)
- `FlowTracker.csproj` - Projektkonfiguration
- `App.xaml`, `App.xaml.cs` - WPF-App-Einstiegspunkt und Tray-Only-Startup

## Voraussetzungen

- Windows 10/11
- .NET SDK 10.x

SDK-Version prüfen:

```powershell
dotnet --info
```

## Build-Prozess

Alle Kommandos im Projektverzeichnis ausführen.

### 1) Restore

```powershell
dotnet restore "FlowTracker.slnx"
```

### 2) Build (Debug)

```powershell
dotnet build "FlowTracker.slnx" -c Debug
```

### 3) Starten

```powershell
dotnet run --project "FlowTracker.csproj" -c Debug
```

Erwartetes Verhalten:

- Kein Hauptfenster in der Taskleiste
- Tray-Icon ist sichtbar
- Beenden über das Tray-Kontextmenü

### 4) Release-Build

```powershell
dotnet build "FlowTracker.slnx" -c Release
```

### 5) Publish (framework-dependent)

```powershell
dotnet publish "FlowTracker.csproj" -c Release -r win-x64 --self-contained false
```

Ausgabe liegt typischerweise unter:
`bin/Release/net10.0-windows/win-x64/publish`

## Native AOT Hinweis

Für WPF mit aktuellem .NET SDK kann `PublishAot=true` zu `NETSDK1168` führen.
Daher ist `PublishAot` aktuell bewusst auf `false` gesetzt.

Sobald WPF-AOT in der Toolchain stabil unterstützt ist, kann der AOT-Workflow wieder aktiviert und hier dokumentiert werden.

## Wartung der README

Diese Datei soll bei jedem relevanten Architektur- oder Build-Änderungsschritt aktualisiert werden, insbesondere bei:

- Änderungen an Ziel-Framework oder SDK-Anforderungen
- neuen Runtime-Parametern (`-r`, `--self-contained`)
- Aktivierung/Änderung von AOT-Strategien
- neuen Entwicklungs-/Betriebsabhängigkeiten

## Datenschutz

- Datenspeicherung ausschließlich lokal via SQLite
- Kein geplanter Versand von Nutzungsdaten über das Netzwerk

## Tray-Icons

- Eigene `.ico`-Dateien liegen unter `Icons/`
- Build kopiert sie automatisch in das Ausgabeverzeichnis
- Finale Zuordnung:
  - Aktiv: `TimeTrackerGreen.ico` (Fallback: `TimeTrackerSchwarz.ico`)
  - Idle/Pause: `TimeTrackerOrange.ico` (Fallback: Aktiv-Icon)
  - Ghost Mode: `TimeTrackerGrau.ico` (Fallback: `TimeTrackerTransparent.ico`, dann `TimeTrackerOrange.ico`, sonst Aktiv-Icon)
  - Aufgabenabhängig (optional, wenn Datei vorhanden):
    - `Meeting` -> `TimeTrackerRed.ico`
    - `Admin` -> `TimeTrackerPurple.ico`
    - `Support`/`Projekt` -> `TimeTrackerBlue.ico`
    - `Arbeit` -> `TimeTrackerGreen.ico`
    - `Pause` -> `TimeTrackerOrange.ico`
- Hinweis:
  - `TimeTrackerTransparent.ico` wird nicht mehr als Standard für sichtbare Zustände verwendet, um ein unsichtbares Tray-Icon zu vermeiden.

## Schritt-2 Status (Win32 Hooks / Idle)

Aktuell implementiert:

- Win32-P/Invoke Basis in `Interop/NativeMethods.cs`
  - `GetLastInputInfo` für Idle-Dauer
  - `GetCursorPos` für Mauskoordinaten
  - optionale Hook-Signaturen: `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`
- Ressourcenschonender Idle-Monitor in `Services/IdleMonitorService.cs`
  - `PeriodicTimer` mit 250ms Polling
  - Idle-Schwelle profilbasiert (`quiet`/`balanced`/`strict`, Default `balanced` = 8s)
  - Events für Idle-Statuswechsel und Mauspositionsänderung
- Integration in `App.xaml.cs`
  - Start des Monitors beim App-Startup
  - geordneter Shutdown via `DisposeAsync`

## Schritt-3 Status (Orbital Overlay)

Aktuell implementiert:

- `Views/OrbitalWindow.xaml`
  - rahmenloses Overlay (`WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`)
  - Ring-UI mit vier Quadranten (`Projekt 1`, `Meeting`, `Pause`, `Admin`)
  - Storyboard-Animationen für Fade-/Scale-In und Fade-/Scale-Out
- `Views/OrbitalWindow.xaml.cs`
  - Positionierung am Mausankerpunkt
  - sofortiges Ausblenden, wenn Maus den aktiven Ringbereich schnell verlässt
  - Quadranten-Click-Event (`QuadrantSelected`)
  - Umschaltung von `WS_EX_TRANSPARENT` (klickbar vs. durchklickbar)
- `Interop/NativeMethods.cs`
  - erweitert um `GetWindowLongPtr` und `SetWindowLongPtr` für Ex-Styles
- `App.xaml.cs`
  - Overlay-/Reminder-Entscheidung läuft über zentrale Display-Policy mit No-Show-Reason-Codes
  - Bei Idle erscheint zunächst ein dezenter Dot (unten rechts), Eskalation auf Orbital erfolgt adaptiv
  - Overlay verschwindet wieder bei Aktivität

## Schritt-4 Status (Datenbank + MVVM)

Aktuell implementiert:

- SQLite-Datenmodell `TimeEntries` mit Feldern:
  - `Id`, `UserId`, `StartTime`, `EndTime`, `Category`, `Description`, `CreatedAt`, `IsDeleted`
- Initialisierung und Performance-Basics:
  - `Infrastructure/DatabaseInitializer.cs` erstellt Schema und Indizes
  - WAL + `synchronous=NORMAL` für ressourcenschonendes lokales Logging
- Repositories mit Dapper:
  - `StartTrackingAsync`
  - `StopTrackingAsync`
  - `GetEntriesAsync`
  - `UpdateEntryAsync`
  - `DeleteEntryAsync` (Soft Delete via `IsDeleted = 1`)
- MVVM:
  - `ViewModels/ViewModelBase.cs` (`INotifyPropertyChanged`)
  - `ViewModels/TrackingViewModel.cs` mit heutiger Entry-Liste und Status
- App-Integration:
  - Datenbankpfad lokal unter `%LocalAppData%/FlowTracker/flowtracker.db`
  - Quadranten-Click startet Tracking-Kategorie über ViewModel
  - Tray-Menü enthält `Tracking stoppen`

## Schritt-5 Status (Dashboard + Reporting)

Aktuell implementiert:

- Dashboard-Fenster `Views/DashboardWindow.xaml` mit Tages-/Wochen-/Monats-/Jahresansicht
- Chronik als editierbare DataGrid-Liste (Start, Ende, Kategorie, Beschreibung)
- KPI-Karten für Soll/Ist:
  - `Sollzeit`
  - `Überstunden`
  - `Fehlstunden`
- Tägliche Saldo-Chronik:
  - Tabelle mit `Tag`, `Ist`, `Soll`, `Saldo`, `kumuliert`
- UI-CRUD:
  - Zeile speichern (Update)
  - Zeile soft-löschen (`IsDeleted = 1`)
- Reporting:
  - CSV-Export
  - PDF-Export (QuestPDF)
  - Export enthält zusätzlich Dauer je Eintrag sowie Summary-Kennzahlen (Produktiv/Soll/Über/Fehl)
- Tray-Integration:
  - `Dashboard öffnen`
  - `Ghost Mode` (stoppt Tracking sofort, Overlay bleibt aus)
  - Linksklick auf Tray-Icon öffnet ein kompaktes Flyout nahe Taskleiste

## Regelwerk (Chronologische Führung)

Aktuell umgesetzt im Zustandsautomaten (`Services/WorkStateMachine.cs`) und `TrackingViewModel`:

- Tagesstart nur einmal pro Tag (`StartWork` wird nach bereits gesetztem Tagesstart blockiert)
- Erlaubte Folge: `OffDuty -> Working -> Break -> Working -> Ended`
- Aktionen werden zustandsabhängig freigegeben; ungültige Aktionen liefern einen konkreten "Nächster Schritt"-Hinweis
- Mittagspause (`BreakFixed`) hat eine Mindestdauer von 30 Minuten vor `ResumeWork`
- Nach Tagesende sind keine neuen Tracking-Aktionen mehr erlaubt
- Session-State wird nach App-/ViewModel-Neustart aus den heutigen DB-Einträgen rekonstruiert

## Sollzeit & Saldo

Aktuell implementiert:

- `BreakPolicies.TargetWorkMinutes` (Default: 480 Minuten / 8h)
- Berechnung produktiver Zeit (Pausen exkludiert)
- Workday-basierte Sollzeitberechnung (Mo-Fr) je gewähltem Zeitraum
- Saldo-Ableitung:
  - Überstunden (`+`)
  - Fehlstunden (`-`)

## Logging & Stabilität

- Dateibasiertes Logging über `Services/AppLogger.cs`
- Log-Datei: `%LocalAppData%/FlowTracker/logs/app.log`
- Globale Exception-Handler aktiv:
  - `DispatcherUnhandledException`
  - `AppDomain.CurrentDomain.UnhandledException`
  - `TaskScheduler.UnobservedTaskException`
- Kritische UI- und Tracking-Events schreiben Fehler und Status ins Log
- Orbital-Overlay-Stabilisierung:
  - Interaktions-Pin-Fenster in `App.xaml.cs` verhindert zu frühes Schließen beim ersten Mausweg zur Auswahl
  - Distanz-/Debounce-Logik in `Views/OrbitalWindow.xaml.cs` nutzt globale Cursorposition (statt nur Window-MouseMove)
  - Fallback auf Rohmenü, falls Filterung temporär keine auswählbaren Aktionen liefert
  - Telemetrie zu Show/Hide-Gründen, Sichtdauer und Quick-Dismiss-Serien im Log (`app.log`)
  - Adaptives Hide-Tuning: bei wiederholten schnellen Abbrüchen erhöht das Overlay temporär Grace/Debounce automatisch
  - Dynamische Reopen-Suppression berücksichtigt Quick-Dismiss-Streak und ignorierte Reminder
  - Focus-aware Suppression (erste Ausbaustufe): Orbital/Reminder werden bei Vollbild und bei fokussensitiven Prozessen unterdrückt (`devenv`, `code`, `rider64`, `winword`, `excel`, `powerpnt`, `teams`, `ms-teams`)
- Zeitformat-Härtung:
  - Repository persistiert UTC-Zeiten als ISO-8601 (`O`) in SQLite
  - robustes Parse-Fallback für ältere Bestandswerte

## Reminder-Profile (neu)

- Profilsteuerung über Umgebungsvariable `FLOWTRACKER_REMINDER_PROFILE`
- Verfügbare Profile:
  - `quiet` (zurückhaltend, längere Idle-/Cooldown-Zeiten)
  - `balanced` (Default)
  - `strict` (frühere und häufigere Erinnerung)
- Beispiel in PowerShell:

```powershell
$env:FLOWTRACKER_REMINDER_PROFILE = "quiet"
dotnet run --project "FlowTracker.csproj" -c Debug
```

## Tests

- Testprojekt: `FlowTracker.Tests` (xUnit)
- In der Solution (`FlowTracker.slnx`) enthalten
- Aktuelle Unit-Tests:
  - `EditableTimeEntryRow.TryBuildTimeEntry` (valid/invalid Parsing)
  - `DashboardViewModel.CalculateRange` (Tag/Woche)
  - `DashboardViewModel.CountWorkdays` und `CalculateTargetDuration` (Sollzeitberechnung)

Tests ausführen:

```powershell
dotnet test "FlowTracker.slnx" -c Debug
```
