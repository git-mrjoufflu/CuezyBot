using Automation.Engine.Variables;

namespace Automation.Tests;

public sealed class NumericVariableExpressionTests
{
    [Theory]
    [InlineData("25", 10, 25)]
    [InlineData("=-5", 10, -5)]
    [InlineData("+5", 10, 15)]
    [InlineData("-2", 10, 8)]
    [InlineData("x2", 10, 20)]
    [InlineData("×1.5", 10, 15)]
    [InlineData("/2", 10, 5)]
    [InlineData("/4", 10, 3)]
    public void AppliesSupportedExpressions(string expression, long current, long expected)
    {
        Assert.True(NumericVariableExpression.TryApply(expression, current, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("x nope")]
    [InlineData("/0")]
    [InlineData("/nope")]
    public void RejectsInvalidExpressions(string expression) =>
        Assert.False(NumericVariableExpression.TryApply(expression, 10, out _));
}
