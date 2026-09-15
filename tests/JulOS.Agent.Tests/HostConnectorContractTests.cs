using System.Security.Cryptography;
using System.Text;

using JulOS.Contracts.HostConnectors;

namespace JulOS.Agent.Tests;

/// <summary>
/// HCON-001 locks the Host Connector contract constants before HCON-002 renames the legacy
/// Agent onto them. These tests fail if the committed constants and the running Agent
/// implementation ever disagree, which is the whole point of locking them in advance.
/// </summary>
[TestClass]
public sealed class HostConnectorContractTests
{
    [TestMethod]
    public void MachineIdentityNamespaceMatchesTheRunningAgentImplementation()
    {
        // JulOS.Agent computes UTF8("JulOS.Agent\0" + source). An enrolled machine must keep
        // its identity across the rename, so this prefix is data compatibility and is never
        // localized, re-cased or renamed to Host Connector.
        const string Source = "0123456789abcdef";

        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("JulOS.Agent\0" + Source)));

        var namespaceBytes = Encoding.UTF8.GetBytes(HostConnectorMigration.MachineIdentityNamespaceV1);
        var sourceBytes = Encoding.UTF8.GetBytes(Source);
        var combined = new byte[namespaceBytes.Length + sourceBytes.Length];
        namespaceBytes.CopyTo(combined, 0);
        sourceBytes.CopyTo(combined, namespaceBytes.Length);

        Assert.AreEqual(expected, Convert.ToHexStringLower(SHA256.HashData(combined)));
    }

    [TestMethod]
    public void MachineIdentityNamespaceKeepsItsExactHistoricalBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(HostConnectorMigration.MachineIdentityNamespaceV1);

        CollectionAssert.AreEqual(
            "JulOS.Agent"u8.ToArray().Append((byte)0).ToArray(),
            bytes,
            "The identity-v1 namespace must keep its historical bytes, including the trailing NUL.");
    }

    [TestMethod]
    public void EveryRuntimeEndpointStaysUnderTheRuntimePrefix()
    {
        string[] endpoints =
        [
            HostConnectorProtocol.Runtime.Enrollment,
            HostConnectorProtocol.Runtime.Heartbeat,
            HostConnectorProtocol.Runtime.HostMetricObservations,
            HostConnectorProtocol.Runtime.NextRequest,
            HostConnectorProtocol.Runtime.RequestResultTemplate,
            HostConnectorProtocol.Runtime.CredentialRotationAttemptTemplate,
            HostConnectorProtocol.Runtime.CredentialRotationAcknowledgementTemplate,
        ];

        foreach (var endpoint in endpoints)
        {
            Assert.StartsWith(HostConnectorProtocol.RuntimePrefix, endpoint);
        }
    }

    [TestMethod]
    public void NoContractSurfaceExposesAGenericCommandOrDestination()
    {
        // docs/HOST_CONNECTOR.md sections 6 and 11: a generic host.command, shell.execute,
        // arbitrary TCP proxy or arbitrary Docker request is prohibited by decision.
        foreach (var prohibited in HostConnectorCapabilities.Prohibited)
        {
            Assert.IsFalse(
                HostConnectorCapabilities.All.Contains(prohibited, StringComparer.Ordinal),
                $"'{prohibited}' must never become a Host Connector capability.");
        }

        string[] surfaces =
        [
            .. HostConnectorProtocol.Runtime.Enrollment.Split('/'),
            .. HostConnectorProtocol.Runtime.NextRequest.Split('/'),
            .. HostConnectorCapabilities.All,
        ];

        foreach (var segment in surfaces)
        {
            Assert.IsFalse(
                segment.Contains("shell", StringComparison.OrdinalIgnoreCase)
                    || segment.Contains("exec", StringComparison.OrdinalIgnoreCase),
                $"'{segment}' names a shell or exec surface.");
        }
    }

    [TestMethod]
    public void ErrorCodesAreUniqueAndNamespaced()
    {
        var codes = HostConnectorErrorCodes.All;

        CollectionAssert.AllItemsAreUnique(codes.ToArray());
        foreach (var code in codes)
        {
            Assert.StartsWith("host_connector.", code);
        }
    }

    [TestMethod]
    public void PermissionMigrationOnlyWidensFromGlobalAuthorizationRights()
    {
        foreach (var (source, targets) in HostConnectorMigration.PermissionMigration)
        {
            Assert.StartsWith("core.authorization.", source);
            foreach (var target in targets)
            {
                CollectionAssert.Contains(HostConnectorPermissions.All.ToArray(), target);
            }
        }

        CollectionAssert.AreEquivalent(
            new[] { HostConnectorPermissions.Read },
            HostConnectorMigration.PermissionMigration["core.authorization.read"].ToArray(),
            "A read-only assignment must never gain manage or diagnostics rights.");
    }

    [TestMethod]
    public void TableRenamesCoverEveryLegacyAgentTableExactlyOnce()
    {
        var renames = HostConnectorMigration.TableRenames;

        CollectionAssert.AllItemsAreUnique(renames.Values.ToArray());
        foreach (var source in renames.Keys)
        {
            Assert.StartsWith("agent", source);
        }

        Assert.AreEqual(
            "legacy_agent_commands",
            renames["agent_commands"],
            "Legacy command rows move to read-only retention and are never copied into the typed request table.");

        foreach (var target in renames.Values.Where(value => value != "legacy_agent_commands"))
        {
            Assert.StartsWith("host_connector", target);
        }
    }
}
