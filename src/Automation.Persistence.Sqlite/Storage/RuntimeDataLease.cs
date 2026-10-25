namespace Automation.Persistence.Sqlite.Storage;

/// <summary>Exclusive ownership of a portable data directory for the lifetime of the runtime.</summary>
public sealed class RuntimeDataLease : IDisposable
{
    private readonly FileStream file;
    private RuntimeDataLease(FileStream file) => this.file = file;

    public static RuntimeDataLease Acquire(AppDataPaths paths)
    {
        paths.EnsureWritable();
        try
        {
            return new RuntimeDataLease(new FileStream(Path.Combine(paths.DataDirectory, "runtime.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Ce dossier de données est déjà utilisé par une autre instance de CuezyBot. / This data folder is already used by another CuezyBot instance.", exception);
        }
    }

    public void Dispose() => file.Dispose();
}
