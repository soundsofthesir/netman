using NetMan.Core;

namespace NetMan.App.Ui;

/// <summary>Edits one profile. Errors show beside each field once it has been edited, or all of them after Save.</summary>
internal sealed class EditDialog : Form
{
    public const string NameInUse = "Name in use";

    private readonly Profile _initial;
    private readonly Func<string, bool> _nameTaken;
    private readonly HashSet<ProfileField> _touched = [];
    private readonly Dictionary<ProfileField, Label> _errors = [];
    private readonly TextBox _name = Field();
    private readonly TextBox _notes = Field(260);
    private readonly RadioButton _static = new() { Text = "Static", AutoSize = true, Margin = new Padding(0, 0, 12, 0) };
    private readonly RadioButton _dhcp = new() { Text = "DHCP", AutoSize = true, Margin = new Padding(0) };
    private readonly TextBox _ip = Field();
    private readonly TextBox _mask = Field();
    private readonly TextBox _gateway = Field();
    private readonly TextBox _dns1 = Field();
    private readonly TextBox _dns2 = Field();
    private readonly TextBox _ping = Field();
    private bool _showAll;
    private bool _filling;

    public EditDialog(Profile initial, string adapterName, Func<string, bool> nameTaken)
    {
        _initial = initial;
        _nameTaken = nameTaken;
        Result = initial;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = string.IsNullOrWhiteSpace(initial.Name) ? "New profile" : "Edit profile";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(14, 14), Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));

        AddField(grid, "Name", _name, ProfileField.Name);
        AddRow(grid, "Notes", _notes, span: 2);
        AddRow(grid, "Adapter", Theme.Label(adapterName), span: 2);
        AddSeparator(grid);

        var mode = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
        mode.Controls.Add(_static);
        mode.Controls.Add(_dhcp);
        AddRow(grid, "", mode, span: 2);

        AddField(grid, "IP", _ip, ProfileField.Ip);
        AddField(grid, "Mask", _mask, ProfileField.Mask);
        AddField(grid, "Gateway", _gateway, ProfileField.Gateway);
        AddField(grid, "DNS 1", _dns1, ProfileField.Dns1);
        AddField(grid, "DNS 2", _dns2, ProfileField.Dns2);
        AddSeparator(grid);
        AddField(grid, "Ping", _ping, ProfileField.PingTarget);

        var save = Theme.Button("Save");
        Theme.Primary(save);
        save.Margin = new Padding(0);
        var cancel = Theme.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 3);
        grid.RowCount++;

        Controls.Add(grid);
        AcceptButton = save;
        CancelButton = cancel;

        Fill(initial);
        save.Click += (_, _) => TrySave();
        _static.CheckedChanged += (_, _) => { UpdateEnabled(); Revalidate(); };
        ResumeLayout(performLayout: true);
    }

    /// <summary>The edited profile, valid when the dialog returns OK.</summary>
    public Profile Result { get; private set; }

    private void Fill(Profile p)
    {
        _filling = true;
        _name.Text = p.Name;
        _notes.Text = p.Notes;
        _static.Checked = p.Mode == AddressMode.Static;
        _dhcp.Checked = p.Mode == AddressMode.Dhcp;
        _ip.Text = p.Ip;
        _mask.Text = p.Mask;
        _gateway.Text = p.Gateway;
        _dns1.Text = p.Dns1;
        _dns2.Text = p.Dns2;
        _ping.Text = p.PingTarget;
        _filling = false;
        UpdateEnabled();
    }

    private Profile Build() => _initial with
    {
        Name = _name.Text.Trim(),
        Notes = _notes.Text.Trim(),
        Mode = _static.Checked ? AddressMode.Static : AddressMode.Dhcp,
        Ip = _ip.Text.Trim(),
        Mask = _mask.Text.Trim(),
        Gateway = _gateway.Text.Trim(),
        Dns1 = _dns1.Text.Trim(),
        Dns2 = _dns2.Text.Trim(),
        PingTarget = _ping.Text.Trim(),
    };

    private List<ValidationError> Errors(Profile p)
    {
        var errors = ProfileValidator.Validate(p).ToList();
        if (p.Name.Length > 0 && _nameTaken(p.Name))
        {
            errors.Add(new ValidationError(ProfileField.Name, NameInUse));
        }

        return errors;
    }

    private void TrySave()
    {
        _showAll = true;
        var profile = Build();
        if (Revalidate(profile))
        {
            Result = profile;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void Revalidate() => Revalidate(Build());

    private bool Revalidate(Profile profile)
    {
        var errors = Errors(profile);
        foreach (var (field, label) in _errors)
        {
            var error = errors.FirstOrDefault(e => e.Field == field);
            label.Text = error is not null && (_showAll || _touched.Contains(field)) ? error.Message : "";
        }

        return errors.Count == 0;
    }

    private void UpdateEnabled()
    {
        foreach (var box in new[] { _ip, _mask, _gateway, _dns1, _dns2 })
        {
            box.Enabled = _static.Checked;
        }
    }

    private void AddField(TableLayoutPanel grid, string label, TextBox box, ProfileField field)
    {
        var error = Theme.Label(color: Theme.Fail);
        error.Anchor = AnchorStyles.Left;
        error.Margin = new Padding(8, 0, 0, 0);
        error.AutoEllipsis = true;
        _errors[field] = error;
        box.AccessibleName = label.Length > 0 ? label : field.ToString();
        box.TextChanged += (_, _) =>
        {
            if (_filling)
            {
                return;
            }

            _touched.Add(field);
            Revalidate();
        };

        AddRow(grid, label, box, span: 1);
        grid.Controls.Add(error, 2, grid.RowCount - 1);
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control, int span)
    {
        var row = grid.RowCount;
        var caption = Theme.Label(label, Color.FromArgb(0x3D, 0x3D, 0x3D));
        caption.Anchor = AnchorStyles.Left;
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(0, 0, 0, 6);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(control, 1, row);
        grid.SetColumnSpan(control, span);
        grid.RowCount = row + 1;
    }

    private static void AddSeparator(TableLayoutPanel grid)
    {
        var line = new Label { Height = 1, BackColor = Color.FromArgb(0xD6, 0xD6, 0xD6), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 10) };
        grid.Controls.Add(line, 0, grid.RowCount);
        grid.SetColumnSpan(line, 3);
        grid.RowCount++;
    }

    private static TextBox Field(int width = 160) => new() { Width = width, BorderStyle = BorderStyle.FixedSingle };
}
