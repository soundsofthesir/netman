using NetMan.App.Platform;
using NetMan.App.Ui;
using NetMan.Core;

namespace NetMan.App;

/// <summary>Adapters on the left, the selected adapter's profiles on the right, the last apply below them.</summary>
internal sealed class MainForm : Form
{
    private readonly ProfileStore _store;
    private readonly IAdapterReader _reader;
    private readonly ApplyPipeline _pipeline;
    private readonly AdapterWatcher _watcher = new(AdapterWatcher.DefaultInterval);
    private readonly CancellationTokenSource _closing = new();
    private readonly Font _bold;

    private readonly AdapterList _adapterList = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly Button _reset = Theme.Button("Reset to DHCP", 210);
    private readonly FlowLayoutPanel _profiles = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.Surface,
        Margin = new Padding(0),
    };
    private readonly ResultPanel _result;
    private readonly Button _new = Theme.Button("New", 56);
    private readonly Button _edit = Theme.Button("Edit", 56);
    private readonly Button _duplicate = Theme.Button("Duplicate", 76);
    private readonly Button _copyTo = Theme.Button("Copy to ▾", 76);
    private readonly Button _capture = Theme.Button("Capture", 68);
    private readonly Button _delete = Theme.Button("Delete", 64);
    private readonly ContextMenuStrip _copyMenu = new();

    private ProfileCatalog _catalog = ProfileCatalog.Empty;
    private IReadOnlyList<AdapterInfo> _adapters = [];
    private Dictionary<Guid, LiveConfig?> _live = [];
    private readonly Dictionary<Guid, ApplyReport> _reports = [];
    private Guid? _selectedProfile;
    private string? _loadError;
    private bool _busy;
    private bool _settingAdapters;
    private int _refreshQueued;

    public MainForm(ProfileStore store, IAdapterReader reader, ApplyPipeline pipeline)
    {
        _store = store;
        _reader = reader;
        _pipeline = pipeline;
        _bold = new Font(Font, FontStyle.Bold);
        _result = new ResultPanel(_bold) { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.Gap, 0, 0) };

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NetMan";
        ClientSize = new Size(720, 440);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        Controls.Add(BuildLayout());
        ResumeLayout(performLayout: true);

        LoadProfiles();
        Wire();
    }

    private TableLayoutPanel BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _reset.Margin = new Padding(0, Theme.Gap, 0, 0);
        _reset.Dock = DockStyle.Fill;
        left.Controls.Add(_adapterList, 0, 0);
        left.Controls.Add(_reset, 0, 1);

        var profilesBox = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, Margin = new Padding(0) };
        profilesBox.Controls.Add(_profiles);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        actions.Controls.AddRange([_new, _edit, _duplicate, _copyTo, _capture]);
        _delete.Margin = new Padding(0);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, Theme.Gap, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(actions, 0, 0);
        bar.Controls.Add(_delete, 1, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(profilesBox, 0, 0);
        right.Controls.Add(_result, 0, 1);
        right.Controls.Add(bar, 0, 2);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        return root;
    }

    private void Wire()
    {
        _adapterList.SelectedIndexChanged += (_, _) =>
        {
            if (!_settingAdapters)
            {
                RefreshProfiles();
            }
        };
        _profiles.ClientSizeChanged += (_, _) => FitRows();
        _reset.Click += async (_, _) =>
        {
            if (SelectedRealAdapter() is { } adapter)
            {
                await RunAsync(ApplyPipeline.ResetName, ct => _pipeline.ResetToDhcpAsync(adapter.Id, ct));
            }
        };
        _result.UndoClicked += async (_, _) =>
        {
            if (SelectedRealAdapter() is { } adapter && _reports.TryGetValue(adapter.Id, out var last) && last.CanUndo)
            {
                await RunAsync(ApplyPipeline.UndoName, ct => _pipeline.UndoAsync(last, ct));
            }
        };
        _new.Click += (_, _) => NewProfile();
        _capture.Click += (_, _) => CaptureProfile();
        _edit.Click += (_, _) => WithSelected(EditProfile);
        _duplicate.Click += (_, _) => WithSelected(p =>
        {
            var (next, copy) = _catalog.Duplicate(p.Id);
            Commit(next, copy.Id);
        });
        _copyTo.Click += (_, _) => WithSelected(ShowCopyMenu);
        _delete.Click += (_, _) => WithSelected(DeleteProfile);
        _watcher.Changed += (_, _) => QueueRefresh();
        Shown += (_, _) =>
        {
            if (_loadError is not null)
            {
                Error(_loadError);
            }

            RefreshAll();
        };
    }

    private void LoadProfiles()
    {
        try
        {
            _catalog = new ProfileCatalog(_store.Load());
        }
        catch (ProfileStoreException ex)
        {
            // Keep the unreadable file so later saves never destroy it.
            var copy = _store.FilePath + ".unreadable";
            try
            {
                File.Copy(_store.FilePath, copy, overwrite: true);
                _loadError = $"{ex.Message} A copy is saved as {Path.GetFileName(copy)}.";
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                _loadError = $"{ex.Message} {copyError.Message}";
            }
        }
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1 || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(RefreshAll);
        }
        catch (InvalidOperationException)
        {
            // The window closed between the check and the call.
        }
    }

    private void RefreshAll()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        if (IsDisposed)
        {
            return;
        }

        _adapters = _reader.List();
        _live = _adapters.ToDictionary(a => a.Id, a => _reader.Read(a.Id));

        var synced = _catalog.SyncAdapterNames(_adapters);
        if (synced.Differs(_catalog))
        {
            _catalog = synced;
            Save();
        }

        var items = _adapters.Select(a => new AdapterItem(a.Id, a.Name, DisplayText.AdapterLine(a, _live[a.Id]))).ToList();
        if (_catalog.Orphans(_adapters.Select(a => a.Id)).Count > 0)
        {
            items.Add(new AdapterItem(AdapterItem.NotFoundId, "Not found", ""));
        }

        _settingAdapters = true;
        _adapterList.SetItems(items, _adapterList.SelectedAdapter?.Id);
        _settingAdapters = false;
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        var item = _adapterList.SelectedAdapter;
        var real = item is { IsNotFound: false };
        var profiles = item is null ? []
            : item.IsNotFound ? _catalog.Orphans(_adapters.Select(a => a.Id))
            : _catalog.ForAdapter(item.Id);

        var rows = _profiles.Controls.OfType<ProfileRow>().ToList();
        if (!rows.Select(r => r.Profile.Id).SequenceEqual(profiles.Select(p => p.Id)))
        {
            rows = RebuildRows(rows, profiles);
        }

        if (_selectedProfile is null || profiles.All(p => p.Id != _selectedProfile))
        {
            _selectedProfile = profiles.FirstOrDefault()?.Id;
        }

        var live = real && _live.TryGetValue(item!.Id, out var l) ? l : null;
        for (var i = 0; i < rows.Count; i++)
        {
            var p = profiles[i];
            rows[i].SetState(p, active: live is not null && Verifier.Matches(p, live), canApply: real && !_busy);
            rows[i].Selected = p.Id == _selectedProfile;
        }

        if (!_busy)
        {
            var report = real && _reports.TryGetValue(item!.Id, out var r) ? r : null;
            _result.ShowReport(report, canUndo: report?.CanUndo == true);
        }

        UpdateButtons();
    }

    private List<ProfileRow> RebuildRows(List<ProfileRow> old, IReadOnlyList<Profile> profiles)
    {
        _profiles.SuspendLayout();
        foreach (var row in old)
        {
            _profiles.Controls.Remove(row);
            row.Dispose();
        }

        var rows = new List<ProfileRow>();
        foreach (var profile in profiles)
        {
            var row = new ProfileRow(profile, _bold) { Width = _profiles.ClientSize.Width };
            row.Chosen += (_, _) => SelectProfile(row.Profile.Id);
            row.EditRequested += (_, _) =>
            {
                if (!_busy)
                {
                    EditProfile(row.Profile);
                }
            };
            row.ApplyClicked += async (_, _) => await RunAsync(row.Profile.Name, ct => _pipeline.ApplyAsync(row.Profile, ct));
            _profiles.Controls.Add(row);
            rows.Add(row);
        }

        _profiles.ResumeLayout();
        return rows;
    }

    private void FitRows()
    {
        foreach (var row in _profiles.Controls.OfType<ProfileRow>())
        {
            row.Width = _profiles.ClientSize.Width;
        }
    }

    private void SelectProfile(Guid id)
    {
        _selectedProfile = id;
        foreach (var row in _profiles.Controls.OfType<ProfileRow>())
        {
            row.Selected = row.Profile.Id == id;
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var adapter = SelectedRealAdapter();
        var profile = SelectedProfile();
        var idle = !_busy;

        _new.Enabled = idle && adapter is not null;
        _capture.Enabled = idle && adapter is not null && _live.GetValueOrDefault(adapter.Id) is not null;
        _reset.Enabled = idle && adapter is not null;
        _edit.Enabled = _duplicate.Enabled = _delete.Enabled = idle && profile is not null;
        _copyTo.Enabled = idle && profile is not null && _adapters.Any(a => a.Id != profile.AdapterId);
    }

    private async Task RunAsync(string name, Func<CancellationToken, Task<ApplyReport>> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        RefreshProfiles();
        _result.ShowApplying(name);
        try
        {
            var report = await Task.Run(() => action(_closing.Token));
            _reports[report.AdapterId] = report;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            _busy = false;
        }

        RefreshAll();
    }

    private void NewProfile()
    {
        if (SelectedRealAdapter() is { } adapter)
        {
            EditProfile(new Profile { AdapterId = adapter.Id, AdapterName = adapter.Name, Mode = AddressMode.Static });
        }
    }

    private void CaptureProfile()
    {
        if (SelectedRealAdapter() is { } adapter && _live.GetValueOrDefault(adapter.Id) is { } live)
        {
            EditProfile(NetMan.Core.Capture.ToProfile(live, adapter.Id, adapter.Name, ""));
        }
    }

    private void EditProfile(Profile profile)
    {
        var adapterName = _adapters.FirstOrDefault(a => a.Id == profile.AdapterId)?.Name ?? profile.AdapterName;
        using var dialog = new EditDialog(profile, adapterName, name => _catalog.NameTaken(name, profile.AdapterId, profile.Id));
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Commit(_catalog.Upsert(dialog.Result), dialog.Result.Id);
        }
    }

    private void ShowCopyMenu(Profile profile)
    {
        _copyMenu.Items.Clear();
        foreach (var adapter in _adapters.Where(a => a.Id != profile.AdapterId))
        {
            _copyMenu.Items.Add(adapter.Name, null, (_, _) => Commit(_catalog.CopyTo(profile.Id, adapter).Catalog, profile.Id));
        }

        _copyMenu.Show(_copyTo, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
    }

    private void DeleteProfile(Profile profile)
    {
        var answer = MessageBox.Show(this, $"Delete {profile.Name}?", "NetMan", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            Commit(_catalog.Remove(profile.Id), null);
        }
    }

    /// <summary>Saves the new catalog. If the save fails, the change is dropped so the window always matches the file.</summary>
    private void Commit(ProfileCatalog next, Guid? select)
    {
        var previous = _catalog;
        _catalog = next;
        if (!Save())
        {
            _catalog = previous;
            return;
        }

        _selectedProfile = select;
        RefreshAll();
    }

    private bool Save()
    {
        try
        {
            _store.Save(_catalog.All);
            return true;
        }
        catch (ProfileStoreException ex)
        {
            Error(ex.Message);
            return false;
        }
    }

    private void WithSelected(Action<Profile> action)
    {
        if (!_busy && SelectedProfile() is { } profile)
        {
            action(profile);
        }
    }

    private AdapterInfo? SelectedRealAdapter()
    {
        var item = _adapterList.SelectedAdapter;
        return item is null || item.IsNotFound ? null : _adapters.FirstOrDefault(a => a.Id == item.Id);
    }

    private Profile? SelectedProfile() => _selectedProfile is { } id ? _catalog.Find(id) : null;

    private void Error(string message) => MessageBox.Show(this, message, "NetMan", MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-apply would leave the adapter half changed and the result unknown.
        if (_busy)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _closing.Cancel();
            _closing.Dispose();
            _copyMenu.Dispose();
            _bold.Dispose();
        }

        base.Dispose(disposing);
    }
}
