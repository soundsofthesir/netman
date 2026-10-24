using NetMan.App.Platform;
using NetMan.Core;

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

        var reader = new WindowsAdapterReader();
        var pipeline = new ApplyPipeline(
            reader,
            AppPaths.CreateNetsh(),
            new Pinger(),
            new ApplyLog(AppPaths.Log),
            TimeProvider.System,
            ApplyTimings.Default);

        Application.Run(new MainForm(new ProfileStore(AppPaths.Profiles), reader, pipeline));
    }
}
