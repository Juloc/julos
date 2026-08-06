using System.Globalization;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class LinuxMetricsCollector
{
    private readonly string procStatPath;
    private readonly string procMemInfoPath;
    private readonly string procLoadAveragePath;
    private readonly string diskPath;
    private readonly string procUptimePath;
    private readonly string procNetworkPath;
    private readonly TimeProvider timeProvider;
    private CpuSample? previousCpu;

    internal LinuxMetricsCollector(
        TimeProvider timeProvider,
        string procStatPath = "/proc/stat",
        string procMemInfoPath = "/proc/meminfo",
        string procLoadAveragePath = "/proc/loadavg",
        string diskPath = "/",
        string procUptimePath = "/proc/uptime",
        string procNetworkPath = "/proc/net/dev")
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.procStatPath = procStatPath;
        this.procMemInfoPath = procMemInfoPath;
        this.procLoadAveragePath = procLoadAveragePath;
        this.diskPath = diskPath;
        this.procUptimePath = procUptimePath;
        this.procNetworkPath = procNetworkPath;
    }

    internal async Task<IReadOnlyList<AgentMetricContract>> CollectAsync(
        CancellationToken cancellationToken)
    {
        var observedAt = this.timeProvider.GetUtcNow();
        var metrics = new List<AgentMetricContract>();
        var cpu = await this.ReadCpuAsync(cancellationToken).ConfigureAwait(false);
        metrics.Add(Metric("host.cpu.utilization", this.CpuUtilization(cpu), "ratio", observedAt));

        var memory = await this.ReadMemoryAsync(cancellationToken).ConfigureAwait(false);
        metrics.Add(Metric("host.memory.total_bytes", memory.TotalBytes, "bytes", observedAt));
        metrics.Add(Metric("host.memory.used_bytes", memory.UsedBytes, "bytes", observedAt));

        var load = await this.ReadLoadAsync(cancellationToken).ConfigureAwait(false);
        metrics.Add(Metric("host.load.one", load.One, "load", observedAt));
        metrics.Add(Metric("host.load.five", load.Five, "load", observedAt));
        metrics.Add(Metric("host.load.fifteen", load.Fifteen, "load", observedAt));

        var uptime = await this.ReadUptimeAsync(cancellationToken).ConfigureAwait(false);
        metrics.Add(Metric("host.uptime.seconds", uptime, "seconds", observedAt));

        var disk = this.ReadDisk();
        metrics.Add(Metric("host.disk.total_bytes", disk.TotalBytes, "bytes", observedAt));
        metrics.Add(Metric("host.disk.used_bytes", disk.UsedBytes, "bytes", observedAt));

        var network = await this.ReadNetworkAsync(cancellationToken).ConfigureAwait(false);
        metrics.Add(Metric(
            "host.network.receive_bytes_total",
            network.ReceiveBytes,
            "bytes",
            observedAt));
        metrics.Add(Metric(
            "host.network.transmit_bytes_total",
            network.TransmitBytes,
            "bytes",
            observedAt));
        return metrics;
    }

    private double? CpuUtilization(CpuSample? current)
    {
        if (current is null)
        {
            return null;
        }

        var previous = this.previousCpu;
        this.previousCpu = current;
        if (previous is null)
        {
            return null;
        }

        var totalDelta = current.Total - previous.Total;
        var idleDelta = current.Idle - previous.Idle;
        if (totalDelta <= 0 || idleDelta < 0)
        {
            return null;
        }

        return Math.Clamp(1d - ((double)idleDelta / totalDelta), 0d, 1d);
    }

    private async Task<CpuSample?> ReadCpuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var firstLine = (await File.ReadAllLinesAsync(this.procStatPath, cancellationToken)
                .ConfigureAwait(false)).FirstOrDefault();
            if (firstLine is null || !firstLine.StartsWith("cpu ", StringComparison.Ordinal))
            {
                return null;
            }

            var values = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(value => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : -1)
                .ToArray();
            if (values.Length < 4 || values.Any(value => value < 0))
            {
                return null;
            }

            var total = values.Sum();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            return new CpuSample(total, idle);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<MemorySample> ReadMemoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var line in await File.ReadAllLinesAsync(this.procMemInfoPath, cancellationToken)
                         .ConfigureAwait(false))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var parts = line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0
                    && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kibibytes))
                {
                    values[line[..separator]] = checked(kibibytes * 1024);
                }
            }

            if (!values.TryGetValue("MemTotal", out var total))
            {
                return new MemorySample(null, null);
            }

            var available = values.TryGetValue("MemAvailable", out var availableValue)
                ? availableValue
                : values.TryGetValue("MemFree", out var freeValue)
                    ? freeValue
                    : (long?)null;
            return new MemorySample(total, available is null ? null : Math.Max(0, total - available.Value));
        }
        catch (IOException)
        {
            return new MemorySample(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new MemorySample(null, null);
        }
        catch (OverflowException)
        {
            return new MemorySample(null, null);
        }
    }

    private async Task<LoadSample> ReadLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(this.procLoadAveragePath, cancellationToken)
                .ConfigureAwait(false);
            var values = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return values.Length >= 3
                ? new LoadSample(ParseDouble(values[0]), ParseDouble(values[1]), ParseDouble(values[2]))
                : new LoadSample(null, null, null);
        }
        catch (IOException)
        {
            return new LoadSample(null, null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new LoadSample(null, null, null);
        }
    }

    private async Task<double?> ReadUptimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(this.procUptimePath, cancellationToken)
                .ConfigureAwait(false);
            var value = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var uptime = value is null ? null : ParseDouble(value);
            return uptime is >= 0 ? uptime : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DiskSample ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(this.diskPath);
            return drive.IsReady
                ? new DiskSample(drive.TotalSize, Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace))
                : new DiskSample(null, null);
        }
        catch (IOException)
        {
            return new DiskSample(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new DiskSample(null, null);
        }
    }

    private async Task<NetworkSample> ReadNetworkAsync(CancellationToken cancellationToken)
    {
        try
        {
            ulong receiveBytes = 0;
            ulong transmitBytes = 0;
            var interfaces = 0;
            foreach (var line in await File.ReadAllLinesAsync(this.procNetworkPath, cancellationToken)
                         .ConfigureAwait(false))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var interfaceName = line[..separator].Trim();
                if (interfaceName.Length == 0 || string.Equals(interfaceName, "lo", StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = line[(separator + 1)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 16
                    || !ulong.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var received)
                    || !ulong.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out var transmitted))
                {
                    return new NetworkSample(null, null);
                }

                receiveBytes = checked(receiveBytes + received);
                transmitBytes = checked(transmitBytes + transmitted);
                interfaces++;
            }

            return interfaces == 0
                ? new NetworkSample(null, null)
                : new NetworkSample(receiveBytes, transmitBytes);
        }
        catch (IOException)
        {
            return new NetworkSample(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new NetworkSample(null, null);
        }
        catch (OverflowException)
        {
            return new NetworkSample(null, null);
        }
    }

    private static AgentMetricContract Metric(
        string name,
        double? value,
        string unit,
        DateTimeOffset observedAt) => new(
            name,
            value,
            unit,
            new Dictionary<string, string>(StringComparer.Ordinal),
            observedAt);

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private sealed record CpuSample(long Total, long Idle);
    private sealed record MemorySample(double? TotalBytes, double? UsedBytes);
    private sealed record LoadSample(double? One, double? Five, double? Fifteen);
    private sealed record DiskSample(double? TotalBytes, double? UsedBytes);
    private sealed record NetworkSample(double? ReceiveBytes, double? TransmitBytes);
}
