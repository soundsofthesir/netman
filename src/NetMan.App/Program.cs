using NetMan.App.Platform;

namespace NetMan.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!Elevation.IsAdministrator())
        {
            MessageBox.Show("NetMan needs to run as administrator.", "NetMan", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm());
    }
}
