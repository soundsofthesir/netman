namespace NetMan.App.Ui;

/// <summary>An adapter in the left list. <see cref="NotFoundId"/> marks the group of profiles whose adapter is missing.</summary>
internal sealed record AdapterItem(Guid Id, string Name, string Line)
{
    public static readonly Guid NotFoundId = Guid.Empty;

    public bool IsNotFound => Id == NotFoundId;

    public override string ToString() => $"{Name}, {Line}";
}

/// <summary>Two-line owner-drawn list: adapter name over its live address or state.</summary>
internal sealed class AdapterList : ListBox
{
    private const int LinePadding = 6;

    public AdapterList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.FixedSingle;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        UpdateItemHeight();
    }

    public AdapterItem? SelectedAdapter => SelectedItem as AdapterItem;

    /// <summary>Replaces the items, keeping the selection by id. Only rebuilds when the set of adapters changed.</summary>
    public void SetItems(IReadOnlyList<AdapterItem> items, Guid? select)
    {
        var current = Items.Cast<AdapterItem>().ToList();
        BeginUpdate();
        if (current.Select(i => i.Id).SequenceEqual(items.Select(i => i.Id)))
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (current[i] != items[i])
                {
                    Items[i] = items[i];
                }
            }
        }
        else
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }

        var index = items.ToList().FindIndex(i => i.Id == select);
        SelectedIndex = index >= 0 ? index : (items.Count > 0 ? 0 : -1);
        EndUpdate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateItemHeight();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not AdapterItem item)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selection : Theme.Surface))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        var x = e.Bounds.Left + LogicalToDeviceUnits(10);
        var top = e.Bounds.Top + LogicalToDeviceUnits(LinePadding);
        var width = e.Bounds.Width - LogicalToDeviceUnits(14);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, item.Name, Font, new Rectangle(x, top, width, Font.Height), Theme.Text, flags);
        TextRenderer.DrawText(e.Graphics, item.Line, Font, new Rectangle(x, top + Font.Height, width, Font.Height), selected ? Theme.Text : Theme.Muted, flags);

        if ((e.State & DrawItemState.Focus) != 0)
        {
            e.DrawFocusRectangle();
        }
    }

    private void UpdateItemHeight() => ItemHeight = Math.Min(255, (Font.Height * 2) + (LinePadding * 2 * DeviceDpi / 96));
}
