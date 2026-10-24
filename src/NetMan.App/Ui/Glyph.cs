using System.ComponentModel;
using System.Drawing.Drawing2D;
using NetMan.Core;

namespace NetMan.App.Ui;

internal enum GlyphKind
{
    None,
    Tick,
    Cross,
    Pending,
    DotOn,
    DotOff,
}

/// <summary>A small drawn status mark: tick, cross, pending ring, or the active-profile dot.</summary>
internal sealed class Glyph : Control
{
    private GlyphKind _kind;

    public Glyph()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Size = new Size(12, 12);
        Margin = new Padding(0, 2, 6, 0);
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GlyphKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
            {
                return;
            }

            _kind = value;
            AccessibleName = value switch
            {
                GlyphKind.Tick => "Pass",
                GlyphKind.Cross => "Fail",
                GlyphKind.Pending => "Checking",
                GlyphKind.DotOn => "Active",
                _ => null,
            };
            Invalidate();
        }
    }

    public static GlyphKind For(CheckStatus status) => status switch
    {
        CheckStatus.Pass => GlyphKind.Tick,
        CheckStatus.Fail => GlyphKind.Cross,
        _ => GlyphKind.Pending,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        var stroke = Math.Max(1.4f, w / 8f);

        switch (_kind)
        {
            case GlyphKind.Tick:
                using (var pen = new Pen(Theme.Pass, stroke))
                {
                    g.DrawLines(pen, [new PointF(w * 0.12f, h * 0.55f), new PointF(w * 0.4f, h * 0.8f), new PointF(w * 0.9f, h * 0.2f)]);
                }

                break;
            case GlyphKind.Cross:
                using (var pen = new Pen(Theme.Fail, stroke))
                {
                    g.DrawLine(pen, w * 0.18f, h * 0.18f, w * 0.82f, h * 0.82f);
                    g.DrawLine(pen, w * 0.82f, h * 0.18f, w * 0.18f, h * 0.82f);
                }

                break;
            case GlyphKind.Pending:
                using (var pen = new Pen(Theme.Muted, 1f))
                {
                    g.DrawEllipse(pen, w * 0.2f, h * 0.2f, w * 0.6f, h * 0.6f);
                }

                break;
            case GlyphKind.DotOn:
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    g.FillEllipse(brush, w * 0.17f, h * 0.17f, w * 0.66f, h * 0.66f);
                }

                break;
            case GlyphKind.DotOff:
                using (var pen = new Pen(Theme.Border, 1f))
                {
                    g.DrawEllipse(pen, w * 0.17f, h * 0.17f, w * 0.66f, h * 0.66f);
                }

                break;
        }
    }
}
