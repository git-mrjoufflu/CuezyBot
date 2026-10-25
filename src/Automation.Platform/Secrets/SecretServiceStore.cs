using System.ComponentModel;
using System.Diagnostics;
using Automation.Contracts.Secrets;

namespace Automation.Platform.Secrets;

internal sealed class SecretServiceStore : ISecretStore
{
    public async Task StoreAsync(
        string key,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);

        using var process = StartProcess("store", "--label=CuezyBot", "service", "CuezyBot", "account", key);
        await process.StandardInput.WriteAsync(secret.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await EnsureSuccessAsync(process, cancellationToken);
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        using var process = StartProcess("lookup", "service", "CuezyBot", "account", key);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : null;
    }

    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        using var process = StartProcess("clear", "service", "CuezyBot", "account", key);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode is not 0 and not 1)
        {
            throw new InvalidOperationException("Le coffre de secrets n’a pas pu supprimer l’identifiant.");
        }
    }

    private static Process StartProcess(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Impossible de démarrer secret-tool.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Le coffre de secrets Linux n’est pas disponible. Installe secret-tool/libsecret.",
                exception);
        }
    }

    private static async Task EnsureSuccessAsync(Process process, CancellationToken cancellationToken)
    {
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Le coffre de secrets a refusé l’opération."
                    : error.Trim());
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clé du secret est obligatoire.", nameof(key));
        }
    }
}
