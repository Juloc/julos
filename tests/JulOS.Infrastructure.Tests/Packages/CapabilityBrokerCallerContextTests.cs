using System.Text.Json;

using JulOS.Application.Auditing;
using JulOS.Domain.Observability;
using JulOS.Infrastructure.Packages;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class CapabilityBrokerCallerContextTests
{
    private const string CallerPackageId = "de.juloc.test.caller";
    private const string ProviderPackageId = "de.juloc.test.provider";
    private const string CapabilityName = "test.caller-context";
    private const string ContractVersion = "1.0.0";

    [TestMethod]
    public async Task AuthenticatedCallerReachesProviderAndAudit()
    {
        var userId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var audit = new RecordingAuditService();
        var provider = new RecordingProvider();
        var broker = CreateBroker(audit, provider);
        var request = CreateRequest(new CapabilityCallerContext(CallerPackageId, userId));

        var response = await broker.InvokeAsync(CallerPackageId, request).ConfigureAwait(false);

        Assert.IsTrue(response.Succeeded);
        Assert.IsNotNull(provider.ReceivedRequest);
        Assert.AreEqual(CallerPackageId, provider.ReceivedRequest.Caller?.PackageId);
        Assert.AreEqual(userId, provider.ReceivedRequest.Caller?.UserId);
        Assert.AreEqual(1, audit.Records.Count);
        Assert.AreEqual(userId, audit.Records[0].UserId);
        Assert.AreEqual(CallerPackageId, audit.Records[0].SourcePackageId);
        Assert.AreEqual(AuditOutcome.Succeeded, audit.Records[0].Outcome);
    }

    [TestMethod]
    public async Task CallerPackageMismatchIsRejectedBeforeProviderInvocation()
    {
        var audit = new RecordingAuditService();
        var provider = new RecordingProvider();
        var broker = CreateBroker(audit, provider);
        var request = CreateRequest(new CapabilityCallerContext(
            "de.juloc.attacker",
            Guid.Parse("22222222-2222-4222-8222-222222222222")));

        var failure = await Assert.ThrowsExactlyAsync<CapabilityBrokerException>(() =>
            broker.InvokeAsync(CallerPackageId, request)).ConfigureAwait(false);

        Assert.AreEqual("capability.caller_identity_mismatch", failure.Code);
        Assert.IsNull(provider.ReceivedRequest);
        Assert.AreEqual(0, audit.Records.Count);
    }

    [TestMethod]
    public async Task MissingCallerContextReceivesAuthorizedPackageOnlyFallback()
    {
        var audit = new RecordingAuditService();
        var provider = new RecordingProvider();
        var broker = CreateBroker(audit, provider);

        var response = await broker.InvokeAsync(
            CallerPackageId,
            CreateRequest(caller: null)).ConfigureAwait(false);

        Assert.IsTrue(response.Succeeded);
        Assert.IsNotNull(provider.ReceivedRequest);
        Assert.AreEqual(CallerPackageId, provider.ReceivedRequest.Caller?.PackageId);
        Assert.IsNull(provider.ReceivedRequest.Caller?.UserId);
        Assert.AreEqual(1, audit.Records.Count);
        Assert.IsNull(audit.Records[0].UserId);
    }

    private static CapabilityBroker CreateBroker(
        RecordingAuditService audit,
        RecordingProvider provider)
    {
        var broker = new CapabilityBroker(audit, TimeProvider.System);
        broker.SetPackageGrants(CallerPackageId, [CapabilityName]);
        broker.Register(ProviderPackageId, provider);
        return broker;
    }

    private static CapabilityRequest CreateRequest(CapabilityCallerContext? caller) => new(
        CapabilityName,
        ContractVersion,
        "read",
        "caller-context-test",
        JsonSerializer.SerializeToElement(new { value = "safe" }),
        DateTimeOffset.UtcNow.AddMinutes(1),
        caller);

    private sealed class RecordingProvider : ICapabilityProvider
    {
        public CapabilityProviderDescriptor Descriptor { get; } = new(
            ProviderPackageId,
            CapabilityName,
            ContractVersion,
            Priority: 100,
            Healthy: true);

        internal CapabilityRequest? ReceivedRequest { get; private set; }

        public Task<CapabilityResponse> InvokeAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ReceivedRequest = request;
            return Task.FromResult(new CapabilityResponse(
                Succeeded: true,
                ErrorCode: null,
                ErrorDetail: null,
                JsonSerializer.SerializeToElement(new { accepted = true })));
        }
    }

    private sealed class RecordingAuditService : IAuditService
    {
        internal List<AuditRecord> Records { get; } = [];

        public void Stage(AuditRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            this.Records.Add(record);
        }

        public Task AppendAsync(
            AuditRecord record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Stage(record);
            return Task.CompletedTask;
        }

        public Task<AuditPageSnapshot> QueryAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AuditPageSnapshot([], NextCursor: null));
        }
    }
}
