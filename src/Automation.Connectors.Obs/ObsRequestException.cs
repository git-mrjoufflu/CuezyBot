namespace Automation.Connectors.Obs;

public sealed class ObsRequestException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
