using Microsoft.Data.Sqlite;

namespace Automation.Persistence.Sqlite.Storage;

public static class SqliteBackups
{
    /// <summary>Restore only while the application is closed. Returns the safety backup path.</summary>
    public static string? RestoreOffline(AppDataPaths paths, string backupPath)
    {
        var sourcePath = Path.GetFullPath(backupPath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Sauvegarde introuvable. / Backup not found.", sourcePath);
        if (string.Equals(sourcePath, paths.DatabasePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choisis une sauvegarde distincte de la base active. / Choose a backup distinct from the active database.");
        using var lease = RuntimeDataLease.Acquire(paths);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        using (var check = source.CreateCommand())
        {
            check.CommandText = "PRAGMA integrity_check;";
            if (check.ExecuteScalar()?.ToString() != "ok") throw new InvalidDataException("Sauvegarde endommagée. / Damaged backup.");
            check.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(check.ExecuteScalar());
            if (version is not (4 or 5)) throw new InvalidDataException("Schéma de sauvegarde non pris en charge (version 4 ou 5 requise). / Unsupported backup schema (version 4 or 5 required).");
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('workflows','workflow_revisions','event_inbox','execution_requests','execution_steps','persistent_variables','application_settings');";
            if (Convert.ToInt32(check.ExecuteScalar()) != 7) throw new InvalidDataException("Ce fichier n’est pas une sauvegarde CuezyBot complète. / This file is not a complete CuezyBot backup.");
        }
        string? safetyBackup = File.Exists(paths.DatabasePath) ? Create(paths, "beforeRestore") : null;
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        using var disable = destination.CreateCommand();
        disable.CommandText = "UPDATE workflows SET is_active = 0; UPDATE execution_requests SET status = 'Expired', completed_at = $now, error = 'Expired after backup restoration.' WHERE status IN ('Pending','Running');";
        disable.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        disable.ExecuteNonQuery();
        return safetyBackup;
    }

    public static string Create(AppDataPaths paths, string reason = "manual")
    {
        paths.EnsureWritable();
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        return Create(source, paths.BackupsDirectory, reason);
    }

    internal static string Create(SqliteConnection source, string directory, string reason)
    {
        Directory.CreateDirectory(directory);
        var safeReason = string.Concat(reason.Where(char.IsAsciiLetterOrDigit));
        var target = Path.Combine(directory, $"automation-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safeReason}-{Guid.NewGuid():N}.db");
        var temporary = target + ".partial";
        try
        {
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
                using var command = destination.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("Backup integrity check failed.");
            }
            File.Move(temporary, target);
            return target;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }
}
