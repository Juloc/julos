namespace JulOS.Infrastructure.Browser;

/// <summary>Serializes Browser runtime creation so one idempotency key cannot race secret/runtime setup.</summary>
public sealed class BrowserSessionCoordinator : IDisposable
{
    private readonly SemaphoreSlim createLock = new(1, 1);

    /// <summary>Enters the Browser session-creation critical section.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await this.createLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(this.createLock);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.createLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? gate;

        internal Releaser(SemaphoreSlim gate)
        {
            this.gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref this.gate, null)?.Release();
        }
    }
}
