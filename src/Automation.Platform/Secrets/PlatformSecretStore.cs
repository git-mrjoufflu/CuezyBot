using Automation.Contracts.Secrets;

namespace Automation.Platform.Secrets;

public static class PlatformSecretStore
{
    public static ISecretStore Create() => OperatingSystem.IsWindows()
        ? new WindowsCredentialStore()
        : OperatingSystem.IsLinux()
            ? new SecretServiceStore()
            : throw new PlatformNotSupportedException(
                "Le coffre de secrets CuezyBot n’est pas encore disponible sur cette plateforme.");
}
