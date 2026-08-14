using System.Text.RegularExpressions;

using JulOS.Infrastructure.WebApps;

namespace JulOS.Infrastructure.Tests.WebApps;

[TestClass]
public sealed class WebAppOriginCodecTests
{
    private const string Zone = "p.localtest.me";

    [TestMethod]
    [DataRow("https://192.168.1.10:8443")]
    [DataRow("http://nas.local:80")]
    [DataRow("https://grafana.lan:3000")]
    [DataRow("https://unifi:443")]
    [DataRow("http://[::1]:8080")]
    public void RoundTripsAnOriginThroughTheProxyHost(string originText)
    {
        var origin = new Uri(originText);

        var host = WebAppOriginCodec.EncodeHost(origin, Zone);
        Assert.IsNotNull(host);
        Assert.IsTrue(WebAppOriginCodec.TryDecodeHost(host, Zone, out var decoded));

        Assert.AreEqual(origin.Scheme, decoded.Scheme);
        Assert.AreEqual(origin.Host, decoded.Host);
        Assert.AreEqual(origin.Port, decoded.Port);
    }

    [TestMethod]
    public void EncodesToASingleLowercaseBase32LabelUnderTheZone()
    {
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), Zone);

        Assert.IsNotNull(host);
        StringAssert.Matches(host!, new Regex(@"^wa[a-z2-7]+\.p\.localtest\.me$"));
        Assert.IsTrue(host!.Split('.')[0].Length <= 63);
    }

    [TestMethod]
    public void DecodesRegardlessOfHostCaseFolding()
    {
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), Zone)!;

        Assert.IsTrue(WebAppOriginCodec.TryDecodeHost(host.ToUpperInvariant(), Zone, out var decoded));
        Assert.AreEqual(8443, decoded.Port);
    }

    [TestMethod]
    public void CanonicalizesTheDefaultPort()
    {
        Assert.AreEqual(
            WebAppOriginCodec.EncodeHost(new Uri("http://nas.local"), Zone),
            WebAppOriginCodec.EncodeHost(new Uri("http://nas.local:80"), Zone));
    }

    [TestMethod]
    public void RejectsHostsThatAreNotValidProxyHosts()
    {
        var host = WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), Zone)!;

        Assert.IsFalse(WebAppOriginCodec.TryDecodeHost(host, "other.zone", out _), "wrong zone");
        Assert.IsFalse(WebAppOriginCodec.TryDecodeHost($"extra.{host}", Zone, out _), "more than one label");
        Assert.IsFalse(WebAppOriginCodec.TryDecodeHost($"nowa.{Zone}", Zone, out _), "missing marker");
        Assert.IsFalse(WebAppOriginCodec.TryDecodeHost($"wa!!!.{Zone}", Zone, out _), "non-base32 label");
        Assert.IsFalse(WebAppOriginCodec.TryDecodeHost(Zone, Zone, out _), "no label in front of the zone");
    }

    [TestMethod]
    public void ReturnsNullWhenTheAuthorityDoesNotFitOneLabel()
    {
        var longHost = new string('a', 60) + ".example.test";

        Assert.IsNull(WebAppOriginCodec.EncodeHost(new Uri($"https://{longHost}:8443"), Zone));
    }

    [TestMethod]
    public void ThrowsWhenTheUriCarriesMoreThanAnOrigin()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => WebAppOriginCodec.EncodeLabel(new Uri("https://host:8443/path")));
        Assert.ThrowsExactly<ArgumentException>(
            () => WebAppOriginCodec.EncodeLabel(new Uri("https://host:8443/?query=1")));
    }

    [TestMethod]
    public void MatchesTheSharedGoldenVector()
    {
        // Cross-language pin shared with src/JulOS.Desktop/src/webapp-browser.test.ts so the C#
        // and TypeScript encoders cannot drift.
        Assert.AreEqual(
            "waaeytsmroge3dqlrrfyytaorygq2dg.p.localtest.me",
            WebAppOriginCodec.EncodeHost(new Uri("https://192.168.1.10:8443"), Zone));
    }
}
