using System.Reflection;

using JulOS.Application.Remote;
using JulOS.Contracts.Remote;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Remote;

[TestClass]
public sealed class RemoteSessionContractValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 3, 21, 30, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(RemoteProtocolIds.Rdp, 3389)]
    [DataRow(RemoteProtocolIds.Vnc, 5900)]
    [DataRow(RemoteProtocolIds.Ssh, 22)]
    public void SupportedProtocolsValidate(string protocol, int port)
    {
        var validator = CreateValidator();

        var validated = validator.ValidateCreate(CreateRequest(protocol, port));

        Assert.AreEqual(protocol, validated.Protocol);
        Assert.AreEqual("host.example.test", validated.Target.Host);
        Assert.AreEqual(port, validated.Target.Port);
        Assert.AreEqual(64, RemoteSessionContractValidator.ComputeRequestIdentity(validated).Length);
    }

    [TestMethod]
    public void UnsupportedProtocolFailsClosed()
    {
        var failure = Assert.ThrowsExactly<RemoteSessionContractException>(() =>
            CreateValidator().ValidateCreate(CreateRequest("telnet", 23)));

        Assert.AreEqual(RemoteSessionFailureCodes.ProtocolUnsupported, failure.Code);
    }

    [TestMethod]
    [DataRow("https://host.example.test")]
    [DataRow("user@host.example.test")]
    [DataRow("host.example.test/path")]
    [DataRow("host.example.test:3389")]
    public void TargetCannotEmbedTransportOrCredentials(string host)
    {
        var request = CreateRequest(RemoteProtocolIds.Rdp, 3389) with
        {
            Target = new RemoteTargetContract(host, 3389),
        };

        var failure = Assert.ThrowsExactly<RemoteSessionContractException>(() =>
            CreateValidator().ValidateCreate(request));

        Assert.AreEqual(RemoteSessionFailureCodes.TargetInvalid, failure.Code);
    }

    [TestMethod]
    public void CredentialMaterialCannotReplaceSecretReference()
    {
        var request = CreateRequest(RemoteProtocolIds.Ssh, 22) with
        {
            SecretReferenceId = Guid.Empty,
        };

        var failure = Assert.ThrowsExactly<RemoteSessionContractException>(() =>
            CreateValidator().ValidateCreate(request));

        Assert.AreEqual(RemoteSessionFailureCodes.CredentialUnavailable, failure.Code);
    }

    [TestMethod]
    public void InvalidViewportAndTimeoutsFailClosed()
    {
        var invalidViewport = CreateRequest(RemoteProtocolIds.Vnc, 5900) with
        {
            Viewport = new RemoteViewportContract(100, 100, 1m),
        };
        var invalidTimeout = CreateRequest(RemoteProtocolIds.Vnc, 5900) with
        {
            IdleTimeoutSeconds = 600,
            MaximumSessionSeconds = 300,
        };

        Assert.AreEqual(
            "remote.viewport_invalid",
            Assert.ThrowsExactly<RemoteSessionContractException>(() =>
                CreateValidator().ValidateCreate(invalidViewport)).Code);
        Assert.AreEqual(
            "remote.timeout_order_invalid",
            Assert.ThrowsExactly<RemoteSessionContractException>(() =>
                CreateValidator().ValidateCreate(invalidTimeout)).Code);
    }

    [TestMethod]
    public void ExpiredOrUnboundedRequestDeadlineFailsClosed()
    {
        var expired = CreateRequest(RemoteProtocolIds.Rdp, 3389) with
        {
            DeadlineUtc = Now.AddSeconds(-1),
        };
        var unbounded = CreateRequest(RemoteProtocolIds.Rdp, 3389) with
        {
            DeadlineUtc = Now.AddMinutes(11),
        };

        Assert.AreEqual(
            RemoteSessionFailureCodes.RequestExpired,
            Assert.ThrowsExactly<RemoteSessionContractException>(() =>
                CreateValidator().ValidateCreate(expired)).Code);
        Assert.AreEqual(
            "remote.request_deadline_invalid",
            Assert.ThrowsExactly<RemoteSessionContractException>(() =>
                CreateValidator().ValidateCreate(unbounded)).Code);
    }

    [TestMethod]
    public void RequestIdentityRequiresAnExactlyMatchingRequest()
    {
        var validator = CreateValidator();
        var first = validator.ValidateCreate(CreateRequest(RemoteProtocolIds.Rdp, 3389));
        var replay = validator.ValidateCreate(CreateRequest(RemoteProtocolIds.Rdp, 3389));
        var changed = validator.ValidateCreate(CreateRequest(RemoteProtocolIds.Rdp, 3390));

        Assert.AreEqual(
            RemoteSessionContractValidator.ComputeRequestIdentity(first),
            RemoteSessionContractValidator.ComputeRequestIdentity(replay));
        Assert.AreNotEqual(
            RemoteSessionContractValidator.ComputeRequestIdentity(first),
            RemoteSessionContractValidator.ComputeRequestIdentity(changed));
    }

    [TestMethod]
    public void LifecycleTransitionsAreExplicitAndTerminalStatesCannotResume()
    {
        RemoteSessionContractValidator.ValidateTransition(
            RemoteSessionStates.Requested,
            RemoteSessionStates.Provisioning);
        RemoteSessionContractValidator.ValidateTransition(
            RemoteSessionStates.Connected,
            RemoteSessionStates.Disconnecting);

        var failure = Assert.ThrowsExactly<RemoteSessionContractException>(() =>
            RemoteSessionContractValidator.ValidateTransition(
                RemoteSessionStates.Failed,
                RemoteSessionStates.Connecting));

        Assert.AreEqual(RemoteSessionFailureCodes.StateTransitionInvalid, failure.Code);
        Assert.IsTrue(RemoteSessionStates.IsTerminal(RemoteSessionStates.Failed));
    }

    [TestMethod]
    public void ListAndCancellationContractsAreBounded()
    {
        var list = RemoteSessionContractValidator.ValidateList(new ListRemoteSessionsRequest(
            [RemoteSessionStates.Connected, RemoteSessionStates.Connected, RemoteSessionStates.Requested],
            50,
            "cursor_01"));
        var cancel = RemoteSessionContractValidator.ValidateCancel(new CancelRemoteSessionRequest(
            Guid.CreateVersion7(),
            "cancel:01",
            3,
            "  User closed the window.  "));

        CollectionAssert.AreEqual(
            new[] { RemoteSessionStates.Connected, RemoteSessionStates.Requested },
            list.States.ToArray());
        Assert.AreEqual("User closed the window.", cancel.Reason);
    }

    [TestMethod]
    public void PublicSessionContractsContainNoCredentialMaterialFields()
    {
        var forbidden = new[] { "password", "token", "privatekey", "credential" };
        var contractTypes = typeof(CreateRemoteSessionRequest).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "JulOS.Contracts.Remote")
            .ToArray();

        var unsafeProperties = contractTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => forbidden.Any(value =>
                property.Name.Contains(value, StringComparison.OrdinalIgnoreCase)))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), unsafeProperties);
    }

    private static RemoteSessionContractValidator CreateValidator() =>
        new(new FakeTimeProvider(Now));

    private static CreateRemoteSessionRequest CreateRequest(string protocol, int port) => new(
        "session:operation:01",
        protocol,
        new RemoteTargetContract("HOST.EXAMPLE.TEST", port),
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        ProfileId: null,
        NetworkProfileId: null,
        new RemoteViewportContract(1920, 1080, 1.25m),
        IdleTimeoutSeconds: 900,
        MaximumSessionSeconds: 7200,
        RequestedAtUtc: Now,
        DeadlineUtc: Now.AddMinutes(2));
}
