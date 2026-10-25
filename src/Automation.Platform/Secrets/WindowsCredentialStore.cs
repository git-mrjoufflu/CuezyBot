using System.Runtime.InteropServices;
using Automation.Contracts.Secrets;

namespace Automation.Platform.Secrets;

internal sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = System.Text.Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 5120)
        {
            throw new ArgumentException("Le secret dépasse la taille acceptée par Windows.", nameof(secret));
        }

        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(key),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "CuezyBot"
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Windows Credential Manager a refusé l’écriture ({Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(Target(key), CredentialTypeGeneric, 0, out var pointer))
        {
            return Marshal.GetLastWin32Error() == 1168
                ? Task.FromResult<string?>(null)
                : throw new InvalidOperationException(
                    $"Windows Credential Manager a refusé la lecture ({Marshal.GetLastWin32Error()}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Task.FromResult<string?>(Marshal.PtrToStringUni(
                credential.CredentialBlob,
                credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(Target(key), CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != 1168)
        {
            throw new InvalidOperationException(
                $"Windows Credential Manager a refusé la suppression ({Marshal.GetLastWin32Error()}).");
        }

        return Task.CompletedTask;
    }

    private static string Target(string key) => $"CuezyBot:{key}";

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
