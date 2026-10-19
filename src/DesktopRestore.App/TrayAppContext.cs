using System.Diagnostics;
using DesktopRestore.Core;
using DesktopRestore.Core.Display;
using DesktopRestore.Core.Layout;
using DesktopRestore.Core.Settings;
using Microsoft.Win32;

namespace DesktopRestore.App;

/// <summary>
/// Tray-Anwendung: stellt beim Start und bei Monitorwechseln die passende Anordnung wieder her
/// und speichert Änderungen periodisch.
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly LayoutEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly DisplayChangeWindow _window = new();
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new();
    private readonly System.Windows.Forms.Timer _debounceTimer = new();

    private ToolStripMenuItem _workplaceItem = null!;
    private ToolStripMenuItem _renameItem = null!;
    private ToolStripMenuItem _pauseItem = null!;
    private ToolStripMenuItem _autostartItem = null!;
    private ProfilesForm? _profilesForm;

    /// <summary>Fingerprint der Monitor-Konfiguration, zu der die aktuelle Anordnung gehört.</summary>
    private string? _activeFingerprint;
    /// <summary>Name des zuletzt erkannten Profils (wird für ähnliche Arbeitsplätze übernommen).</summary>
    private string? _activeName;
    private DateTime _lastDisplayChange = DateTime.Now;
    private bool _ready;
    private bool _busy;
    private bool _forceOnDebounce;

    public TrayAppContext(AppPaths paths)
    {
        _paths = paths;
        _settings = AppSettings.Load(paths.SettingsFile);
        _engine = new LayoutEngine(new LayoutStore(paths.ProfilesDir, _settings.HistoryLimit), _settings);

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "DesktopRestore",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowProfiles();

        _window.DisplayChanged += OnDisplayChanged;
        _window.WorkAreaChanged += OnWorkAreaChanged;
        _window.ExplorerRestarted += OnExplorerRestarted;
        _window.CloseRequested += ExitApp;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _debounceTimer.Tick += (_, _) => OnDebounceTick();
        _autoSaveTimer.Tick += (_, _) => OnAutoSaveTick();
        ApplyAutoSaveInterval();

        _ = StartupAsync();
    }

    // ---------------------------------------------------------------- Ablauf

    private async Task StartupAsync()
    {
        await RunFlowAsync("Start", waitForStableDesktop: true, apply: _settings.RestoreOnStartup);
        _ready = true;
        _lastDisplayChange = DateTime.Now;
    }

    /// <summary>Erkennt den Arbeitsplatz und stellt (optional) die passende Anordnung wieder her.</summary>
    private async Task RunFlowAsync(string reason, bool waitForStableDesktop, bool apply, bool ignorePause = false)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            if (waitForStableDesktop)
                await WaitForStableDesktopAsync();

            var config = DisplayDetector.GetCurrent();
            _activeFingerprint = config.Fingerprint;
            var match = ProfileMatcher.FindBest(config, _engine.Store.LoadAll());
            Log.Info($"{reason}: {DisplayConfiguration.DescribeDetailed(config.Monitors)} → {(match == null ? "kein Profil" : $"„{match.Profile.Name}“ ({match.Kind})")}");

            if (match == null)
            {
                _activeName = null;
                Notify(_settings.AutoSaveMinutes > 0
                    ? $"Neuer Arbeitsplatz erkannt: {config.DefaultName}. Die Anordnung wird automatisch gespeichert."
                    : $"Neuer Arbeitsplatz erkannt: {config.DefaultName}. Bitte Anordnung speichern.");
                return;
            }

            _activeName = match.Profile.Name;
            if (!apply || (_settings.Paused && !ignorePause))
                return;

            var result = _engine.ApplyProfile(match.Profile, config, match);

            // Explorer sortiert nach Anmeldung/Monitorwechsel teils verzögert nochmals um.
            await Task.Delay(2000);
            int deviations = _engine.CountDeviations(result.Placement!.Targets);
            if (deviations > 0)
            {
                Log.Info($"{deviations} Icons wurden nachträglich verschoben, wende Anordnung erneut an.");
                result = _engine.ApplyProfile(match.Profile, config, match);
            }
            Notify(result.Message, force: reason == "Manuell");
        }
        catch (Exception ex)
        {
            Log.Error($"{reason}: Wiederherstellen fehlgeschlagen", ex);
            Notify($"Wiederherstellen fehlgeschlagen: {ex.Message}", force: true, warning: true);
        }
        finally
        {
            _busy = false;
            UpdateMenu();
        }
    }

    /// <summary>Wartet, bis der Desktop geladen ist und sich die Icons nicht mehr bewegen.</summary>
    private async Task WaitForStableDesktopAsync()
    {
        var deadline = DateTime.Now + StartupTimeout;
        var stableFor = TimeSpan.FromSeconds(Math.Max(1, _settings.StartupStableSeconds));
        string? last = null;
        var stableSince = DateTime.Now;
        while (DateTime.Now < deadline)
        {
            var snapshot = _engine.Icons.TryRead(out _);
            var signature = snapshot == null || snapshot.Icons.Count == 0
                ? null
                : string.Join("|", snapshot.Icons.Select(i => $"{i.Key}:{i.X},{i.Y}"));
            if (signature != null && signature == last)
            {
                if (DateTime.Now - stableSince >= stableFor)
                    return;
            }
            else
            {
                last = signature;
                stableSince = DateTime.Now;
            }
            await Task.Delay(500);
        }
        Log.Warn("Desktop wurde innerhalb der Wartezeit nicht stabil.");
    }

    private void OnDisplayChanged()
    {
        _lastDisplayChange = DateTime.Now;
        _forceOnDebounce = true;
        RestartDebounce();
    }

    private void OnWorkAreaChanged()
    {
        _lastDisplayChange = DateTime.Now;
        RestartDebounce();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            RestartDebounce();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            RestartDebounce();
    }

    private void OnExplorerRestarted()
    {
        Log.Info("Explorer wurde neu gestartet.");
        _lastDisplayChange = DateTime.Now;
        if (_ready)
            _ = RunFlowAsync("Explorer-Neustart", waitForStableDesktop: true, apply: _settings.RestoreOnDisplayChange);
    }

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Interval = Math.Max(1, _settings.DisplayChangeDelaySeconds) * 1000;
        _debounceTimer.Start();
    }

    private void OnDebounceTick()
    {
        _debounceTimer.Stop();
        if (!_ready)
            return; // der Start-Ablauf erkennt den Arbeitsplatz ohnehin
        if (_busy)
        {
            RestartDebounce();
            return;
        }

        bool force = _forceOnDebounce;
        _forceOnDebounce = false;
        var fingerprint = DisplayDetector.GetCurrent().Fingerprint;
        if (!force && fingerprint == _activeFingerprint)
            return;

        Log.Info($"Monitorkonfiguration geändert ({_activeFingerprint} → {fingerprint}).");
        _ = RunFlowAsync("Monitorwechsel", waitForStableDesktop: true, apply: _settings.RestoreOnDisplayChange);
    }

    private void OnAutoSaveTick()
    {
        if (!_ready || _busy || _settings.Paused || _settings.AutoSaveMinutes <= 0)
            return;
        // Direkt nach einem Monitorwechsel könnte Explorer die Icons durcheinandergebracht haben.
        if (DateTime.Now - _lastDisplayChange < TimeSpan.FromSeconds(_settings.AutoSaveQuietSeconds))
            return;

        try
        {
            if (DisplayDetector.GetCurrent().Fingerprint != _activeFingerprint)
            {
                RestartDebounce(); // Wechsel wurde noch nicht verarbeitet
                return;
            }
            bool isNew = _engine.Store.Load(_activeFingerprint!) == null;
            var result = _engine.SaveCurrent(_activeName, force: false);
            if (result.Outcome == SaveOutcome.Saved)
            {
                _activeName = result.Profile!.Name;
                if (isNew)
                    Notify($"Anordnung für „{_activeName}“ gespeichert.");
                UpdateMenu();
                _profilesForm?.RefreshLists();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Automatisches Speichern fehlgeschlagen", ex);
        }
    }

    // ---------------------------------------------------------------- Menüaktionen

    private void SaveNow()
    {
        if (_busy)
            return;
        try
        {
            var result = _engine.SaveCurrent(_activeName, force: true);
            if (result.Outcome == SaveOutcome.NoIcons)
            {
                Notify("Keine Desktop-Icons gefunden.", force: true, warning: true);
                return;
            }
            _activeFingerprint = result.Profile!.Id;
            _activeName = result.Profile.Name;
            Notify($"Anordnung „{_activeName}“ gespeichert ({result.Profile.Icons.Count} Icons).", force: true);
            _profilesForm?.RefreshLists();
        }
        catch (Exception ex)
        {
            Log.Error("Speichern fehlgeschlagen", ex);
            Notify($"Speichern fehlgeschlagen: {ex.Message}", force: true, warning: true);
        }
        UpdateMenu();
    }

    private async void ApplyFromManager(LayoutProfile profile, bool fromHistory)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var config = DisplayDetector.GetCurrent();
            var result = _engine.ApplyProfile(profile, config);
            if (fromHistory && profile.Id == config.Fingerprint)
            {
                // Wiederhergestellten Stand übernehmen; der bisherige landet in der Historie.
                await Task.Delay(1000);
                _engine.SaveCurrent(_activeName, force: false);
            }
            Notify(result.Message, force: true);
        }
        catch (Exception ex)
        {
            Log.Error("Anwenden fehlgeschlagen", ex);
            Notify($"Anwenden fehlgeschlagen: {ex.Message}", force: true, warning: true);
        }
        finally
        {
            _busy = false;
            _profilesForm?.RefreshLists();
            UpdateMenu();
        }
    }

    private void ShowProfiles()
    {
        if (_profilesForm is { IsDisposed: false })
        {
            _profilesForm.Activate();
            return;
        }
        _profilesForm = new ProfilesForm(_engine.Store, () => _activeFingerprint, ApplyFromManager, OnProfilesChanged);
        _profilesForm.FormClosed += (_, _) => _profilesForm = null;
        _profilesForm.Show();
        _profilesForm.Activate();
    }

    private void OnProfilesChanged()
    {
        var active = _activeFingerprint == null ? null : _engine.Store.Load(_activeFingerprint);
        if (active != null)
            _activeName = active.Name;
        UpdateMenu();
    }

    private void RenameActive()
    {
        if (_activeFingerprint == null || _engine.Store.Load(_activeFingerprint) is not { } profile)
            return;
        var name = InputDialog.Show(null, "Arbeitsplatz umbenennen", "Name für diesen Arbeitsplatz:", profile.Name);
        if (name == null)
            return;
        _engine.Store.Rename(profile.Id, name);
        _activeName = name;
        _profilesForm?.RefreshLists();
        UpdateMenu();
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Layouts exportieren",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            FileName = $"DesktopRestore-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        try
        {
            _engine.Store.Export(dialog.FileName);
            Notify($"Layouts exportiert nach {dialog.FileName}.", force: true);
        }
        catch (Exception ex)
        {
            Log.Error("Export fehlgeschlagen", ex);
            Notify($"Export fehlgeschlagen: {ex.Message}", force: true, warning: true);
        }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Layouts importieren",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        if (MessageBox.Show("Vorhandene Layouts für dieselben Arbeitsplätze werden überschrieben. Fortfahren?",
                "Layouts importieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            int count = _engine.Store.Import(dialog.FileName);
            Notify($"{count} Layout(s) importiert.", force: true);
            _profilesForm?.RefreshLists();
            OnProfilesChanged();
        }
        catch (Exception ex)
        {
            Log.Error("Import fehlgeschlagen", ex);
            Notify($"Import fehlgeschlagen: {ex.Message}", force: true, warning: true);
        }
    }

    private void ApplyAutoSaveInterval()
    {
        _autoSaveTimer.Stop();
        if (_settings.AutoSaveMinutes > 0)
        {
            _autoSaveTimer.Interval = _settings.AutoSaveMinutes * 60_000;
            _autoSaveTimer.Start();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_paths.SettingsFile);
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen konnten nicht gespeichert werden", ex);
        }
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        ExitThread();
    }

    // ---------------------------------------------------------------- Menü

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _workplaceItem = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_workplaceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Anordnung jetzt speichern", null, (_, _) => SaveNow());
        menu.Items.Add("Anordnung wiederherstellen", null,
            (_, _) => _ = RunFlowAsync("Manuell", waitForStableDesktop: false, apply: true, ignorePause: true));
        menu.Items.Add(new ToolStripSeparator());
        var manage = new ToolStripMenuItem("Profile verwalten…", null, (_, _) => ShowProfiles());
        manage.Font = new Font(manage.Font, FontStyle.Bold);
        menu.Items.Add(manage);
        _renameItem = new ToolStripMenuItem("Arbeitsplatz umbenennen…", null, (_, _) => RenameActive());
        menu.Items.Add(_renameItem);
        menu.Items.Add("Exportieren…", null, (_, _) => Export());
        menu.Items.Add("Importieren…", null, (_, _) => Import());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildSettingsMenu());
        _pauseItem = new ToolStripMenuItem("Automatik pausieren", null, (_, _) =>
        {
            _settings.Paused = !_settings.Paused;
            SaveSettings();
            UpdateMenu();
        });
        menu.Items.Add(_pauseItem);
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, (_, _) =>
        {
            try
            {
                Autostart.Set(!Autostart.IsEnabled);
            }
            catch (Exception ex)
            {
                Log.Error("Autostart konnte nicht geändert werden", ex);
            }
            UpdateMenu();
        });
        menu.Items.Add(_autostartItem);
        menu.Items.Add("Protokoll öffnen", null, (_, _) => OpenPath(Log.FilePath));
        menu.Items.Add("Datenordner öffnen", null, (_, _) => OpenPath(_paths.Root));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => UpdateMenu();
        return menu;
    }

    private ToolStripMenuItem BuildSettingsMenu()
    {
        var root = new ToolStripMenuItem("Einstellungen");

        var autoSave = new ToolStripMenuItem("Automatisch speichern");
        foreach (var (label, minutes) in new[] { ("Aus", 0), ("Jede Minute", 1), ("Alle 5 Minuten", 5), ("Alle 15 Minuten", 15), ("Alle 30 Minuten", 30) })
        {
            autoSave.DropDownItems.Add(Option(label, () => _settings.AutoSaveMinutes == minutes, () =>
            {
                _settings.AutoSaveMinutes = minutes;
                ApplyAutoSaveInterval();
            }));
        }

        var zone = new ToolStripMenuItem("Neue Icons platzieren");
        foreach (var (label, value) in new[] { ("Rechts, von oben", NewIconZone.RightColumn), ("Links, von oben", NewIconZone.LeftColumn), ("Nicht verschieben", NewIconZone.Keep) })
            zone.DropDownItems.Add(Option(label, () => _settings.NewIconZone == value, () => _settings.NewIconZone = value));

        root.DropDownItems.Add(autoSave);
        root.DropDownItems.Add(zone);
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(Option("Beim Start wiederherstellen", () => _settings.RestoreOnStartup,
            () => _settings.RestoreOnStartup = !_settings.RestoreOnStartup));
        root.DropDownItems.Add(Option("Bei Monitorwechsel wiederherstellen", () => _settings.RestoreOnDisplayChange,
            () => _settings.RestoreOnDisplayChange = !_settings.RestoreOnDisplayChange));
        root.DropDownItems.Add(Option("Benachrichtigungen anzeigen", () => _settings.ShowNotifications,
            () => _settings.ShowNotifications = !_settings.ShowNotifications));
        return root;
    }

    /// <summary>Menüeintrag mit Häkchen; der Zustand wird beim Öffnen über <see cref="RefreshChecks"/> aktualisiert.</summary>
    private ToolStripMenuItem Option(string text, Func<bool> isChecked, Action onClick) =>
        new(text, null, (_, _) =>
        {
            onClick();
            SaveSettings();
        })
        { Tag = isChecked };

    private static void RefreshChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is not ToolStripMenuItem menuItem)
                continue;
            if (menuItem.Tag is Func<bool> isChecked)
                menuItem.Checked = isChecked();
            RefreshChecks(menuItem.DropDownItems);
        }
    }

    private void UpdateMenu()
    {
        bool hasProfile = _activeFingerprint != null && _engine.Store.Load(_activeFingerprint) != null;
        var name = _activeName ?? "neu – noch nicht gespeichert";
        if (_activeName != null && !hasProfile)
            name += " (ähnlich)";
        _workplaceItem.Text = $"Arbeitsplatz: {name}";
        _renameItem.Enabled = hasProfile;
        _pauseItem.Checked = _settings.Paused;
        _autostartItem.Checked = Autostart.IsEnabled;
        RefreshChecks(_tray.ContextMenuStrip!.Items);

        var tip = $"DesktopRestore – {_activeName ?? "neuer Arbeitsplatz"}{(_settings.Paused ? " (pausiert)" : "")}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void Notify(string text, bool force = false, bool warning = false)
    {
        if (!force && !_settings.ShowNotifications)
            return;
        _tray.ShowBalloonTip(4000, "DesktopRestore", text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path)))
            return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(TrayAppContext).Assembly.GetManifestResourceStream("app.ico");
        return stream != null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _autoSaveTimer.Dispose();
            _debounceTimer.Dispose();
            _window.Dispose();
            _profilesForm?.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
