using System.Net;

using JulOS.Infrastructure.WebApps;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Tests.WebApps;

[TestClass]
public sealed class WebAppTargetRegistryTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> OneTarget(
        string host = "unifi.os.juloc.de",
        string upstream = "https://10.0.0.5:8443",
        string? mode = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["WebApps:Targets:0:Host"] = host,
            ["WebApps:Targets:0:Upstream"] = upstream,
        };
        if (mode is not null)
        {
            values["WebApps:Targets:0:RenderingMode"] = mode;
        }

        return values;
    }

    [TestMethod]
    public void EmptyConfigurationYieldsAnEmptyRegistry()
    {
        var registry = WebAppTargetRegistry.Read(Configuration([]));

        Assert.AreEqual(0, registry.Count);
        Assert.IsFalse(registry.TryResolve("unifi.os.juloc.de", out _));
    }

    [TestMethod]
    public void ResolvesALocalTargetByHostIgnoringCaseAndPort()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(OneTarget()));

        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.TryResolve("UNIFI.os.juloc.de:443", out var target));
        Assert.AreEqual("unifi.os.juloc.de", target.Host);
        Assert.AreEqual(new Uri("https://10.0.0.5:8443"), target.Upstream);
        Assert.AreEqual(WebAppRenderingMode.Local, target.RenderingMode);
        Assert.IsFalse(target.RequiresAddressPinning);
    }

    [TestMethod]
    public void DefaultsToLocalRenderingWhenModeIsOmitted()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(OneTarget()));

        Assert.IsTrue(registry.TryResolve("unifi.os.juloc.de", out var target));
        Assert.AreEqual(WebAppRenderingMode.Local, target.RenderingMode);
    }

    [TestMethod]
    public void ResolvesAnAutoTarget()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(OneTarget(mode: "auto")));

        Assert.IsTrue(registry.TryResolve("unifi.os.juloc.de", out var target));
        Assert.AreEqual(WebAppRenderingMode.Auto, target.RenderingMode);
    }

    [TestMethod]
    public void DoesNotResolveAStreamedTarget()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(OneTarget(mode: "streamed")));

        Assert.AreEqual(1, registry.Count);
        Assert.IsFalse(registry.TryResolve("unifi.os.juloc.de", out _));
    }

    [TestMethod]
    public void DoesNotResolveAnUnknownHost()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(OneTarget()));

        Assert.IsFalse(registry.TryResolve("grafana.os.juloc.de", out _));
        Assert.IsFalse(registry.TryResolve(null, out _));
        Assert.IsFalse(registry.TryResolve("   ", out _));
    }

    [TestMethod]
    public void ThrowsWhenTheUpstreamIsNotAnAbsoluteHttpUri()
    {
        var configuration = Configuration(OneTarget(upstream: "not-a-uri"));

        Assert.ThrowsExactly<InvalidOperationException>(() => WebAppTargetRegistry.Read(configuration));
    }

    [TestMethod]
    public void ThrowsWhenTheHostCarriesASchemeOrPort()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => WebAppTargetRegistry.Read(Configuration(OneTarget(host: "https://unifi.os.juloc.de"))));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => WebAppTargetRegistry.Read(Configuration(OneTarget(host: "unifi.os.juloc.de:443"))));
    }

    [TestMethod]
    public void ThrowsOnAnUnknownRenderingMode()
    {
        var configuration = Configuration(OneTarget(mode: "embedded"));

        Assert.ThrowsExactly<InvalidOperationException>(() => WebAppTargetRegistry.Read(configuration));
    }

    [TestMethod]
    public void ThrowsWhenTwoTargetsShareAHost()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["WebApps:Targets:0:Host"] = "unifi.os.juloc.de",
            ["WebApps:Targets:0:Upstream"] = "https://10.0.0.5:8443",
            ["WebApps:Targets:1:Host"] = "UNIFI.os.juloc.de",
            ["WebApps:Targets:1:Upstream"] = "https://10.0.0.6:8443",
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => WebAppTargetRegistry.Read(configuration));
    }

    private static Dictionary<string, string?> DynamicConfig(params string[] allowedHosts)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:CookieDomain"] = ".localtest.me",
            ["WebApps:Dynamic:Enabled"] = "true",
            ["WebApps:Dynamic:ProxyZone"] = "p.localtest.me",
        };
        for (var index = 0; index < allowedHosts.Length; index++)
        {
            values[$"WebApps:Dynamic:AllowedHosts:{index}"] = allowedHosts[index];
        }

        return values;
    }

    [TestMethod]
    public async Task ResolvesAnEncodedAllowedDynamicLiteralToItsPinnedAddress()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig("192.168.0.0/16")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), "p.localtest.me")!;

        Assert.IsTrue(registry.TryResolve(host, out var target));
        Assert.AreEqual(new Uri("https://192.168.1.10:8443/"), target.Upstream);
        Assert.AreEqual(WebAppRenderingMode.Local, target.RenderingMode);
        Assert.IsTrue(target.RequiresAddressPinning);

        var addresses = await registry.ResolveAllowedAddressesAsync(target, CancellationToken.None).ConfigureAwait(false);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("192.168.1.10") }, addresses);
    }

    [TestMethod]
    public async Task DnsDynamicTargetRequiresBothAllowedNameAndAllowedResolvedNetwork()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig(
            "localhost",
            "127.0.0.0/8",
            "::1/128")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("http://localhost:8080"), "p.localtest.me")!;

        Assert.IsTrue(registry.TryResolve(host, out var target));
        Assert.AreEqual(new Uri("http://localhost:8080/"), target.Upstream);

        var addresses = await registry.ResolveAllowedAddressesAsync(target, CancellationToken.None).ConfigureAwait(false);
        Assert.IsGreaterThan(0, addresses.Length);
        Assert.IsTrue(addresses.All(IPAddress.IsLoopback));
    }

    [TestMethod]
    public async Task DnsSuffixWithoutAnAllowedResolvedNetworkFailsClosed()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig("localhost")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("http://localhost:8080"), "p.localtest.me")!;

        Assert.IsTrue(registry.TryResolve(host, out var target));
        var addresses = await registry.ResolveAllowedAddressesAsync(target, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(0, addresses.Length);
    }

    [TestMethod]
    public async Task DnsNameCannotEscapeToAnAddressOutsideItsConfiguredCidr()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig("localhost", "10.0.0.0/8")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("http://localhost:8080"), "p.localtest.me")!;

        Assert.IsTrue(registry.TryResolve(host, out var target));
        var addresses = await registry.ResolveAllowedAddressesAsync(target, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(0, addresses.Length);
    }

    [TestMethod]
    public void ResolvesAnEncodedAllowedDynamicHostByDnsSuffixBeforeAddressValidation()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig(".lan", "192.168.0.0/16")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://grafana.lan:3000"), "p.localtest.me")!;

        Assert.IsTrue(registry.TryResolve(host, out var target));
        Assert.AreEqual(new Uri("https://grafana.lan:3000/"), target.Upstream);
        Assert.IsTrue(target.RequiresAddressPinning);
    }

    [TestMethod]
    public void RejectsADynamicHostThatIsNotOnTheAllowlist()
    {
        var registry = WebAppTargetRegistry.Read(Configuration(DynamicConfig("10.0.0.0/8")));
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), "p.localtest.me")!;

        Assert.IsFalse(registry.TryResolve(host, out _));
    }

    [TestMethod]
    public void DoesNotResolveDynamicHostsWhenDisabled()
    {
        var registry = WebAppTargetRegistry.Read(Configuration([]));
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), "p.localtest.me")!;

        Assert.IsFalse(registry.TryResolve(host, out _));
    }

    [TestMethod]
    public void ThrowsWhenTheProxyZoneIsNotUnderTheCookieDomain()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:CookieDomain"] = ".os.juloc.de",
            ["WebApps:Dynamic:Enabled"] = "true",
            ["WebApps:Dynamic:ProxyZone"] = "p.localtest.me",
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => WebAppTargetRegistry.Read(configuration));
    }
}
