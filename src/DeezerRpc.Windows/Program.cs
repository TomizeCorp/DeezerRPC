namespace DeezerRpc.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "Local\\DeezerRPC.SingleInstance", out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show(
                "DeezerRPC fonctionne déjà dans la zone de notification.",
                "DeezerRPC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

