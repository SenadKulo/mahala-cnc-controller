using Velopack;

namespace CncController.WinForms;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // First: the installer and updates (Velopack) start the program with their own arguments, handled here,
        // and it may exit right away. Without an installation (Visual Studio) it does nothing.
        VelopackApp.Build().Run();

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        // A file passed on the command line ("Open with" in Explorer) is loaded once the window is up.
        if (args.Length > 0 && File.Exists(args[0]))
            form.Shown += (_, _) => form.LoadFile(args[0]);
        Application.Run(form);
    }
}
