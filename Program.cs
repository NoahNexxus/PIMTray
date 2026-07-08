using PIMTray.UI;

namespace PIMTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal("PIM Tray - unhandled exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowFatal("PIM Tray - fatal exception", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception ex)
        {
            ShowFatal("PIM Tray - startup failed", ex);
        }
    }

    private static void ShowFatal(string title, Exception? ex)
    {
        // Show the exception type and message rather than the full ToString(): the latter
        // includes the stack trace and local file paths, which don't help the user and are
        // more than an error dialog should surface.
        var message = ex is null ? "Unknown error." : $"{ex.GetType().Name}: {ex.Message}";
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
