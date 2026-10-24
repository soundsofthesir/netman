using System.ComponentModel;
using NetMan.Core;

namespace NetMan.App.Ui;

/// <summary>One profile: active dot, name, address and its own Apply button.</summary>
internal sealed class ProfileRow : TableLayoutPanel
{
    private readonly Glyph _dot = new() { Anchor = AnchorStyles.Left };
    private readonly Label _name;
    private readonly Label _address = Theme.Label();
    private readonly Button _apply = Theme.Button("Apply", 64);
    private bool _selected;

    public ProfileRow(Profile profile, Font bold)
    {
        Profile = profile;
        ColumnCount = 4;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Height = 44;
        Margin = new Padding(0);
        Padding = new Padding(10, 0, 10, 0);
        BackColor = Theme.Surface;

        _name = Theme.Label();
        _name.Font = bold;
        _name.AutoEllipsis = true;
        _name.AutoSize = false;
        _name.Dock = DockStyle.Fill;
        _name.TextAlign = ContentAlignment.MiddleLeft;
        _address.Anchor = AnchorStyles.Left;
        _apply.Anchor = AnchorStyles.Right;
        _apply.Margin = new Padding(0);

        Controls.Add(_dot, 0, 0);
        Controls.Add(_name, 1, 0);
        Controls.Add(_address, 2, 0);
        Controls.Add(_apply, 3, 0);

        foreach (Control c in new Control[] { this, _dot, _name, _address })
        {
            c.MouseDown += (_, _) => Chosen?.Invoke(this, EventArgs.Empty);
            c.DoubleClick += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        }

        _apply.Click += (_, _) => ApplyClicked?.Invoke(this, EventArgs.Empty);
        _apply.GotFocus += (_, _) => Chosen?.Invoke(this, EventArgs.Empty);
        SetState(profile, active: false, canApply: true);
    }

    public event EventHandler? Chosen;

    public event EventHandler? EditRequested;

    public event EventHandler? ApplyClicked;

    public Profile Profile { get; private set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            BackColor = value ? Theme.Selection : Theme.Surface;
            if (value)
            {
                Theme.Primary(_apply);
            }
            else
            {
                Theme.Secondary(_apply);
            }
        }
    }

    public void SetState(Profile profile, bool active, bool canApply)
    {
        Profile = profile;
        _name.Text = profile.Name;
        _address.Text = DisplayText.ProfileAddress(profile);
        _address.ForeColor = profile.Mode == AddressMode.Dhcp ? Theme.Muted : Theme.Text;
        _dot.Kind = active ? GlyphKind.DotOn : GlyphKind.DotOff;
        _apply.Enabled = canApply;
        _apply.AccessibleName = $"Apply {profile.Name}";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.RowLine);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
