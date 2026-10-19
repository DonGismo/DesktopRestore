# Changelog

Alle nennenswerten Änderungen an diesem Projekt. Format angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionierung nach [Semantic Versioning](https://semver.org/lang/de/).

## [1.0.0] – 2026-10-19

### Hinzugefügt

- Tray-App, die die Anordnung der Desktop-Icons pro Monitor-Konfiguration speichert und wiederherstellt
- Arbeitsplatz-Erkennung über Monitor-Modell (EDID), Auflösung, Position und Skalierung; Fallback auf ähnliche Arbeitsplätze
- Wiederherstellen beim Start (nach stabil geladenem Desktop), bei Monitorwechsel, Entsperren, Standby-Ende und Explorer-Neustart
- Automatisches Speichern mit Sperrfrist nach Monitorwechseln und Historie (letzte 10 Stände pro Arbeitsplatz)
- Einsortieren neuer Icons in die rechte oder linke Rasterspalte; Einpassen in den sichtbaren Bereich
- Profilverwaltung (umbenennen, löschen, anwenden, frühere Stände wiederherstellen)
- Export und Import aller Layouts als ZIP
- Kommandozeile `--save`, `--restore`, `--dump`
- Per-User-MSI (WiX 5) ohne Adminrechte, mit Autostart und Startmenü-Eintrag
- GitHub-Actions-Workflow für Build, Tests und Releases
