using System.Threading.Channels;

using JulOS.Application.Remote;

namespace JulOS.Infrastructure.Remote;

/// <summary>Process-local wake-up signal; durable provisioning state remains in the core database.</summary>
public sealed class RemoteSessionProvisioningSignal : IRemoteSessionProvisioningSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    /// <inheritdoc />
    public void Signal() => this.channel.Writer.TryWrite(true);

    /// <inheritdoc />
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        _ = await this.channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
