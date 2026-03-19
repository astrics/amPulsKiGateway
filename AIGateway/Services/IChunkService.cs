using AiGateway.Api.Models.Internal;
using AiGateway.Api.Models.Requests;

namespace AiGateway.Api.Services;

public interface IChunkService
{
    List<Chunk> Split(List<CustomerStatement> statements);
}
