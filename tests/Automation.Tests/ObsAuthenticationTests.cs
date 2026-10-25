using Automation.Connectors.Obs;

namespace Automation.Tests;

public sealed class ObsAuthenticationTests
{
    [Fact]
    public void ComputeResponse_IsStableAndUsesAllInputs()
    {
        var response = ObsAuthentication.ComputeResponse(
            "mot-de-passe",
            "sel-exemple",
            "defi-exemple");

        Assert.Equal("oVEsCo9TFFEFDtqqjZdniyKJvPKs+Sp4zlMNT2HTWMI=", response);
        Assert.NotEqual(
            response,
            ObsAuthentication.ComputeResponse("autre", "sel-exemple", "defi-exemple"));
    }
}
