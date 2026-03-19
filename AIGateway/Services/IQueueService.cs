using AiGateway.Api.Models.Internal;
using AiGateway.Api.Models.Responses;

namespace AiGateway.Api.Services;

public interface IQueueService
{
    Task<QueueItem> EnqueueAsync(QueueItem item);
    Task<JobStatusResponse?> GetStatusAsync(string jobId);
    int GetPendingCount();
    int GetActiveCount();
    int GetCompletedTotal();
    List<JobStatusResponse> GetAllJobs();
}
