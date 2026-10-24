namespace NetMan.App.Ui;

internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(0xF3, 0xF3, 0xF3);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(0xB5, 0xB5, 0xB5);
    public static readonly Color RowLine = Color.FromArgb(0xEC, 0xEC, 0xEC);
    public static readonly Color Selection = Color.FromArgb(0xCC, 0xE4, 0xF7);
    public static readonly Color Text = Color.FromArgb(0x1B, 0x1B, 0x1B);
    public static readonly Color Muted = Color.FromArgb(0x5C, 0x5C, 0x5C);
    public static readonly Color Accent = Color.FromArgb(0x00, 0x67, 0xC0);
    public static readonly Color AccentHover = Color.FromArgb(0x00, 0x5A, 0xA8);
    public static readonly Color Pass = Color.FromArgb(0x0F, 0x7B, 0x0F);
    public static readonly Color Fail = Color.FromArgb(0xC4, 0x2B, 0x1C);

    /// <summary>Gap between groups, in 96 dpi units. The form scales it.</summary>
    public const int Gap = 8;

    public static void Primary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentHover;
    }

    public static void Secondary(Button button)
    {
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.ForeColor = SystemColors.ControlText;
    }

    public static Button Button(string text, int width = 72)
    {
        var button = new Button { Text = text, AutoSize = false, Size = new Size(width, 26), Margin = new Padding(0, 0, 6, 0) };
        Secondary(button);
        return button;
    }

    public static Label Label(string text = "", Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color ?? Text,
        Margin = new Padding(0),
        UseMnemonic = false,
    };
}
