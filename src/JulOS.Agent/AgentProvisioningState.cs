using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal enum AgentProvisioningStatus
{
    Pending,
    Enrolled,
}

internal sealed record AgentProvisioningState(
    AgentProvisioningStatus Status,
    Guid? AgentId,
    string Credential,
    DateTimeOffset? EnrolledAtUtc,
    int? HeartbeatIntervalSeconds,
    int? CommandPollIntervalSeconds,
    string Name,
    string MachineIdentity,
    string OperatingSystem,
    string Architecture,
    string Version)
{
    internal static async Task<AgentProvisioningState> CreatePendingAsync(
        AgentBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var machineIdentity = await ReadMachineIdentityAsync(
            options.MachineIdentityPath,
            cancellationToken).ConfigureAwait(false);
        var state = new AgentProvisioningState(
            AgentProvisioningStatus.Pending,
            AgentId: null,
            GenerateCredential(),
            EnrolledAtUtc: null,
            HeartbeatIntervalSeconds: null,
            CommandPollIntervalSeconds: null,
            options.Name,
            machineIdentity,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            options.Version);
        state.Validate();
        return state;
    }

    internal AgentProvisioningState Complete(RedeemAgentEnrollmentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.AgentId == Guid.Empty
            || !string.Equals(response.Credential, this.Credential, StringComparison.Ordinal)
            || response.HeartbeatIntervalSeconds is < 5 or > 300
            || response.CommandPollIntervalSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("The enrollment response is invalid.");
        }

        var completed = this with
        {
            Status = AgentProvisioningStatus.Enrolled,
            AgentId = response.AgentId,
            EnrolledAtUtc = response.EnrolledAtUtc,
            HeartbeatIntervalSeconds = response.HeartbeatIntervalSeconds,
            CommandPollIntervalSeconds = response.CommandPollIntervalSeconds,
        };
        completed.Validate();
        return completed;
    }

    internal void Validate()
    {
        ValidateCredential(this.Credential);
        if (string.IsNullOrWhiteSpace(this.Name)
            || this.Name.Length > 128
            || this.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(this.MachineIdentity)
            || this.MachineIdentity.Length is < 8 or > 256
            || string.IsNullOrWhiteSpace(this.OperatingSystem)
            || this.OperatingSystem.Length > 128
            || string.IsNullOrWhiteSpace(this.Architecture)
            || this.Architecture.Length > 128
            || string.IsNullOrWhiteSpace(this.Version)
            || this.Version.Length > 128)
        {
            throw new InvalidOperationException("The Agent identity state contains invalid host facts.");
        }

        if (this.Status == AgentProvisioningStatus.Pending)
        {
            if (this.AgentId is not null
                || this.EnrolledAtUtc is not null
                || this.HeartbeatIntervalSeconds is not null
                || this.CommandPollIntervalSeconds is not null)
            {
                throw new InvalidOperationException("Pending Agent identity state contains enrolled fields.");
            }

            return;
        }

        if (this.AgentId is not Guid agentId
            || agentId == Guid.Empty
            || this.EnrolledAtUtc is null
            || this.HeartbeatIntervalSeconds is < 5 or > 300
            || this.CommandPollIntervalSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("Enrolled Agent identity state is incomplete.");
        }
    }

    private static async Task<string> ReadMachineIdentityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Machine identity file '{path}' does not exist.");
        }

        var information = new FileInfo(path);
        if (information.Length is < 1 or > 4096)
        {
            throw new InvalidOperationException("The machine identity file has an invalid size.");
        }

        var source = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        if (source.Length is < 8 or > 4096 || source.Any(char.IsControl))
        {
            throw new InvalidOperationException("The machine identity file contains invalid data.");
        }

        var bytes = Encoding.UTF8.GetBytes("JulOS.Agent\0" + source);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string GenerateCredential()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        try
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ValidateCredential(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException(),
            };
            var bytes = Convert.FromBase64String(normalized);
            try
            {
                if (bytes.Length != 48)
                {
                    throw new FormatException();
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The Agent credential is invalid.", exception);
        }
    }
}
