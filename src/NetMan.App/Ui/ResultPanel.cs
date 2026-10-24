using NetMan.Core;

namespace NetMan.App.Ui;

/// <summary>The last apply on the selected adapter: name, outcome, time, Undo, one row per check.</summary>
internal sealed class ResultPanel : Panel
{
    private const int Columns = 2;

    private readonly Label _name;
    private readonly Label _outcome;
    private readonly Label _time = Theme.Label(color: Theme.Muted);
    private readonly Label _error = Theme.Label(color: Theme.Fail);
    private readonly Button _undo = Theme.Button("Undo");
    private readonly TableLayoutPanel _checks = new()
    {
        ColumnCount = Columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        Margin = new Padding(0),
    };
    private readonly ToolTip _tips = new();

    public ResultPanel(Font bold)
    {
        BackColor = Theme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        Padding = new Padding(10, 8, 10, 8);
        Height = 128;
        Visible = false;

        _name = Theme.Label();
        _name.Font = bold;
        _outcome = Theme.Label();
        _outcome.Font = bold;
        _error.MaximumSize = new Size(440, 0);
        _undo.Margin = new Padding(0);

        var header = new TableLayoutPanel { ColumnCount = 5, RowCount = 1, Dock = DockStyle.Top, Height = 28, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var label in new[] { _name, _outcome, _time })
        {
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 0, 10, 0);
        }

        header.Controls.Add(_name, 0, 0);
        header.Controls.Add(_outcome, 1, 0);
        header.Controls.Add(_time, 2, 0);
        header.Controls.Add(_undo, 4, 0);

        for (var i = 0; i < Columns; i++)
        {
            _checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Columns));
        }

        _error.Dock = DockStyle.Top;
        _error.Padding = new Padding(0, 0, 0, 4);

        // Dock order is reversed: the last added docks first.
        Controls.Add(_checks);
        Controls.Add(_error);
        Controls.Add(header);

        _undo.Click += (_, _) => UndoClicked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? UndoClicked;

    public void ShowApplying(string name)
    {
        _name.Text = name;
        _outcome.Text = "Applying";
        _outcome.ForeColor = Theme.Muted;
        _time.Text = "";
        _error.Visible = false;
        _undo.Enabled = false;
        SetRows([]);
        Visible = true;
    }

    public void ShowReport(ApplyReport? report, bool canUndo)
    {
        if (report is null)
        {
            Visible = false;
            return;
        }

        _name.Text = report.ProfileName;
        _outcome.Text = DisplayText.Outcome(report.Outcome);
        _outcome.ForeColor = report.Outcome switch
        {
            ApplyOutcome.Applied or ApplyOutcome.Saved => Theme.Pass,
            ApplyOutcome.NoReply => Theme.Text,
            _ => Theme.Fail,
        };
        _time.Text = DisplayText.Time(report);
        _error.Text = report.Error;
        _error.Visible = report.Error.Length > 0;
        _undo.Enabled = canUndo;
        SetRows(DisplayText.Rows(report));
        Visible = true;
    }

    private void SetRows(IReadOnlyList<ResultRow> rows)
    {
        _checks.SuspendLayout();
        foreach (Control c in _checks.Controls)
        {
            _tips.SetToolTip(c, null);
        }

        _checks.Controls.Clear();
        _checks.RowStyles.Clear();
        _checks.RowCount = (rows.Count + Columns - 1) / Columns;
        for (var i = 0; i < rows.Count; i++)
        {
            _checks.Controls.Add(Cell(rows[i]), i % Columns, i / Columns);
        }

        _checks.ResumeLayout();
    }

    private FlowLayoutPanel Cell(ResultRow row)
    {
        var cell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 16, 4) };
        var label = Theme.Label(row.Label, Theme.Muted);
        label.AutoSize = false;
        label.Size = new Size(56, 16);
        var value = Theme.Label(row.Value, row.Status == CheckStatus.Fail ? Theme.Fail : Theme.Text);
        cell.Controls.Add(new Glyph { Kind = Glyph.For(row.Status) });
        cell.Controls.Add(label);
        cell.Controls.Add(value);
        if (row.ToolTip.Length > 0)
        {
            _tips.SetToolTip(value, row.ToolTip);
        }

        cell.AccessibleName = $"{row.Label} {row.Value}";
        return cell;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }
}
