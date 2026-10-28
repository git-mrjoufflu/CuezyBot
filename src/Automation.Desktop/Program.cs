using Avalonia;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--restore-backup", StringComparer.Ordinal))
        {
            var index = Array.IndexOf(args, "--restore-backup");
            if (index + 1 >= args.Length || !args.Contains("--confirm-restore", StringComparer.Ordinal))
            {
                Console.Error.WriteLine("Ferme CuezyBot puis utilise --restore-backup <fichier.db> --confirm-restore. Les données actuelles seront remplacées après sauvegarde de secours. / Close CuezyBot then use --restore-backup <file.db> --confirm-restore. Current data will be replaced after a safety backup.");
                Environment.ExitCode = 2;
                return;
            }
            try
            {
                var safetyBackup = SqliteBackups.RestoreOffline(new AppDataPaths(), args[index + 1]);
                Console.WriteLine("Restauration terminée. Automatisations désactivées. / Restore complete. Automations disabled.");
                if (safetyBackup is not null) Console.WriteLine(safetyBackup);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception.Message); Environment.ExitCode = 1; }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
