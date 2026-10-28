using Automation.Domain.Workflows;
using Automation.Engine.Workflows;
using System.Text.Json.Nodes;

namespace Automation.Tests;

public sealed class ExecutableWorkflowValidatorTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("3601")]
    [InlineData("abc")]
    public void Delay_RejectsInvalidDuration(string value)
    {
        var workflow = Workflow("core.delay", new JsonObject { ["seconds"] = value });
        Assert.Contains(ExecutableWorkflowValidator.Validate(workflow), error => error.Code == "delay.invalid");
    }

    [Fact]
    public void Visibility_RequiresSceneAndSourceSelection()
    {
        var invalid = Workflow("obs.set-source-visible", new JsonObject());
        Assert.Equal(2, ExecutableWorkflowValidator.Validate(invalid).Count);
        var valid = Workflow("obs.set-source-visible", new JsonObject { ["scene"] = "Studio", ["sources"] = new JsonArray("Camera", "Logo"), ["visible"] = false });
        Assert.Empty(ExecutableWorkflowValidator.Validate(valid));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("86401")]
    [InlineData("abc")]
    public void Timer_RejectsInvalidInterval(string value)
    {
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Timer", false,
            [new(Guid.NewGuid(), "core.timer", 1, new JsonObject { ["seconds"] = value }),
             new(Guid.NewGuid(), "core.delay", 1, new JsonObject { ["seconds"] = "0" })], []);
        Assert.Contains(ExecutableWorkflowValidator.Validate(workflow), error => error.Code == "timer.invalid");
    }

    [Fact]
    public void UnsupportedAction_IsExplicitlyRejectedInBothLanguages()
    {
        var workflow = Workflow("core.action", new JsonObject());
        Assert.Contains("pas encore", Assert.Single(ExecutableWorkflowValidator.Validate(workflow)).Message);
        Assert.Contains("not executable", Assert.Single(ExecutableWorkflowValidator.Validate(workflow, true)).Message);
    }

    private static WorkflowDefinition Workflow(string type, JsonObject parameters) => new(Guid.NewGuid(), 1, "Test", false, [new(Guid.NewGuid(), type, 1, parameters)], []);
}
