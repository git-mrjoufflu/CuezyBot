namespace Automation.Persistence.Sqlite.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string? applicationDirectory = null)
    {
        ApplicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        DataDirectory = Path.Combine(ApplicationDirectory, "data");
        DatabasePath = Path.Combine(DataDirectory, "automation.db");
        BackupsDirectory = Path.Combine(DataDirectory, "backups");
    }

    public string ApplicationDirectory { get; }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string BackupsDirectory { get; }

    public void EnsureWritable()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);

        var probePath = Path.Combine(DataDirectory, $".write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Le dossier « {DataDirectory} » n’est pas accessible en écriture. " +
                "Déplacez l’application dans un dossier appartenant à votre compte utilisateur.",
                exception);
        }
    }
}
