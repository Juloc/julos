namespace JulOS.Infrastructure.Packages;

/// <summary>Serializes interactive runtime creation so one idempotency key cannot race resource setup.</summary>
internal sealed class InteractiveSessionCoordinator : IDisposable
{
    private readonly SemaphoreSlim createLock = new(1, 1);

    internal async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await this.createLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(this.createLock);
    }

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
