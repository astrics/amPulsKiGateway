using AiGateway.Sympany.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AiGateway.Sympany.Api.Services;

public sealed class LmStudioConcurrencyGate
{
    private readonly SemaphoreSlim _semaphore;

    public LmStudioConcurrencyGate(IOptions<GatewayOptions> options)
    {
        var maxConcurrency = Math.Max(1, options.Value.MaxConcurrency);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}