using AiGateway.Api.Models.Internal;

namespace AiGateway.Api.Services;

public interface IPromptBuilder
{
    string BuildSystemPrompt(string analysisType);
    string BuildUserPrompt(string analysisType, Chunk chunk, string? customPrompt);
}
