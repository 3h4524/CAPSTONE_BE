using APCS.Application.Features.Assistant.Tools;

namespace APCS.Application.Features.Assistant;

public sealed class AssistantToolRegistry(IEnumerable<IAssistantTool> tools)
{
    public IReadOnlyList<IAssistantTool> Resolve(AssistantToolContext context) =>
        tools.Where(tool => tool.IsAvailable(context)).ToArray();

    public IAssistantConfirmableTool? FindConfirmable(string name) =>
        tools.OfType<IAssistantConfirmableTool>().FirstOrDefault(tool => tool.Name == name);
}
