using DesktopRestore.Core.Display;
using DesktopRestore.Core.Layout;

namespace DesktopRestore.App;

/// <summary>Übersicht der gespeicherten Arbeitsplätze inklusive früherer Stände.</summary>
internal sealed class ProfilesForm : Form
{
    private readonly LayoutStore _store;
    private readonly Func<string?> _activeId;
    private readonly Action<LayoutProfile, bool> _apply;
    private readonly Action _changed;

    private readonly ListView _profiles = CreateList();
    private readonly ListView _history = CreateList();
    private readonly Button _rename = new() { Text = "Umbenennen…", AutoSize = true };
    private readonly Button _delete = new() { Text = "Löschen", AutoSize = true };
    private readonly Button _applyProfile = new() { Text = "Auf Desktop anwenden", AutoSize = true };
    private readonly Button _applyHistory = new() { Text = "Diesen Stand wiederherstellen", AutoSize = true };

    public ProfilesForm(LayoutStore store, Func<string?> activeId, Action<LayoutProfile, bool> apply, Action changed)
    {
        _store = store;
        _activeId = activeId;
        _apply = apply;
        _changed = changed;

        Text = "DesktopRestore – Arbeitsplätze";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(600, 420);
        Padding = new Padding(10);
        Font = SystemFonts.MessageBoxFont ?? Font;

        _profiles.Columns.Add("Name", 220);
        _profiles.Columns.Add("Monitore", 340);
        _profiles.Columns.Add("Icons", 60, HorizontalAlignment.Right);
        _profiles.Columns.Add("Gespeichert", 140);
        _history.Columns.Add("Gespeichert", 180);
        _history.Columns.Add("Icons", 60, HorizontalAlignment.Right);

        var profileButtons = ButtonRow(_rename, _delete, _applyProfile);
        var historyButtons = ButtonRow(_applyHistory);
        var close = new Button { Text = "Schließen", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        CancelButton = close;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Caption("Gespeicherte Arbeitsplätze (fett = aktueller Arbeitsplatz)"));
        layout.Controls.Add(_profiles);
        layout.Controls.Add(profileButtons);
        layout.Controls.Add(Caption("Frühere Stände des ausgewählten Arbeitsplatzes"));
        layout.Controls.Add(_history);
        layout.Controls.Add(historyButtons);
        layout.Controls.Add(ButtonRow(close));
        Controls.Add(layout);

        _profiles.SelectedIndexChanged += (_, _) => OnSelectionChanged();
        _profiles.DoubleClick += (_, _) => Rename();
        _history.SelectedIndexChanged += (_, _) => _applyHistory.Enabled = _history.SelectedItems.Count == 1;
        _rename.Click += (_, _) => Rename();
        _delete.Click += (_, _) => Delete();
        _applyProfile.Click += (_, _) =>
        {
            if (SelectedProfile is { } p)
                _apply(p, false);
        };
        _applyHistory.Click += (_, _) => ApplyHistory();

        RefreshLists();
    }

    private LayoutProfile? SelectedProfile =>
        _profiles.SelectedItems.Count == 1 ? _profiles.SelectedItems[0].Tag as LayoutProfile : null;

    public void RefreshLists()
    {
        var selectedId = SelectedProfile?.Id ?? _activeId();
        var activeId = _activeId();
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var profile in _store.LoadAll())
        {
            var item = new ListViewItem(profile.Name) { Tag = profile };
            item.SubItems.Add(DisplayConfiguration.DescribeDetailed(DisplayConfiguration.SortByPosition(profile.Monitors)));
            item.SubItems.Add(profile.Icons.Count.ToString());
            item.SubItems.Add(profile.SavedAt.ToString("g"));
            if (profile.Id == activeId)
                item.Font = new Font(_profiles.Font, FontStyle.Bold);
            _profiles.Items.Add(item);
            if (profile.Id == selectedId)
                item.Selected = true;
        }
        _profiles.EndUpdate();
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        var profile = SelectedProfile;
        _rename.Enabled = _delete.Enabled = _applyProfile.Enabled = profile != null;

        _history.BeginUpdate();
        _history.Items.Clear();
        if (profile != null)
        {
            foreach (var entry in _store.GetHistory(profile.Id))
            {
                var item = new ListViewItem(entry.SavedAt.ToString("G")) { Tag = entry };
                item.SubItems.Add(entry.IconCount.ToString());
                _history.Items.Add(item);
            }
        }
        _history.EndUpdate();
        _applyHistory.Enabled = false;
    }

    private void Rename()
    {
        if (SelectedProfile is not { } profile)
            return;
        var name = InputDialog.Show(this, "Arbeitsplatz umbenennen", "Name für diesen Arbeitsplatz:", profile.Name);
        if (name == null)
            return;
        _store.Rename(profile.Id, name);
        _changed();
        RefreshLists();
    }

    private void Delete()
    {
        if (SelectedProfile is not { } profile)
            return;
        if (MessageBox.Show(this, $"Arbeitsplatz „{profile.Name}“ inklusive aller früheren Stände löschen?",
                "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _store.Delete(profile.Id);
        _changed();
        RefreshLists();
    }

    private void ApplyHistory()
    {
        if (_history.SelectedItems.Count != 1 || _history.SelectedItems[0].Tag is not HistoryEntry entry)
            return;
        var profile = _store.LoadHistory(entry);
        if (profile != null)
            _apply(profile, true);
    }

    private static ListView CreateList() => new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 4, 0, 0) };
        panel.Controls.AddRange(Enumerable.Reverse(buttons).ToArray());
        return panel;
    }
}
