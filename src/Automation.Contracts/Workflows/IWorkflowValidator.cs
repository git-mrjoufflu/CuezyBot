using Automation.Domain.Workflows;

namespace Automation.Contracts.Workflows;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(WorkflowDefinition workflow);
}
