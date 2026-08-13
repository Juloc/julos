namespace JulOS.Architecture.Tests;

[TestClass]
public sealed class RemoteProviderRuntimeTests
{
    [TestMethod]
    public void ProviderExchangesJsonAuthDataBeforeOpeningTheTunnel()
    {
        var runtimeDirectory = Path.Combine(
            Repository.Root,
            "packages",
            "JulOS.Remote",
            "runtime");
        var bridge = File.ReadAllText(
            Path.Combine(runtimeDirectory, "JulOS.Remote.ProviderBridge", "Program.cs"));
        var nginx = File.ReadAllText(Path.Combine(runtimeDirectory, "nginx.conf.template"));

        StringAssert.Contains(bridge, "http://127.0.0.1:8080/api/tokens");
        StringAssert.Contains(bridge, "new FormUrlEncodedContent");
        StringAssert.Contains(bridge, "[\"data\"] = encryptedData");
        StringAssert.Contains(bridge, "ExchangeGuacamoleAuthTokenAsync(token.EncryptedData)");
        StringAssert.Contains(bridge, "\"authToken\"");
        StringAssert.Contains(bridge, "Uri.EscapeDataString(authToken)");
        StringAssert.Contains(bridge, "set $julos_guac_auth_token");
        Assert.IsFalse(
            bridge.Contains(
                "set $julos_guac_token \\\"{token.EncryptedData}\\\"",
                StringComparison.Ordinal),
            "Encrypted JSON-auth data must never be used as the WebSocket tunnel token.");
        StringAssert.Contains(
            nginx,
            "proxy_pass http://127.0.0.1:8080/websocket-tunnel?token=$julos_guac_auth_token;");
    }

    [TestMethod]
    public void ProviderReportsConnectedOnlyAfterDisplayListenerReadiness()
    {
        var runtimeDirectory = Path.Combine(
            Repository.Root,
            "packages",
            "JulOS.Remote",
            "runtime");
        var bridge = File.ReadAllText(
            Path.Combine(runtimeDirectory, "JulOS.Remote.ProviderBridge", "Program.cs"));
        var launcher = File.ReadAllText(Path.Combine(runtimeDirectory, "remote-provider-runtime.sh"));

        var finalizeStart = bridge.IndexOf(
            "static async Task FinalizeAsync",
            StringComparison.Ordinal);
        var exchangeStart = bridge.IndexOf(
            "static async Task<string> ExchangeGuacamoleAuthTokenAsync",
            StringComparison.Ordinal);
        Assert.IsTrue(finalizeStart >= 0 && exchangeStart > finalizeStart);
        var finalizeBody = bridge[finalizeStart..exchangeStart];
        Assert.IsFalse(
            finalizeBody.Contains("ReportConnectedAsync", StringComparison.Ordinal),
            "Token preparation must not mark the provider connected.");

        var readinessLoop = launcher.IndexOf(
            "while ! nc -z 127.0.0.1 \"$JULOS_PROVIDER_LISTEN_PORT\"; do",
            StringComparison.Ordinal);
        Assert.IsTrue(readinessLoop >= 0, "The provider listener readiness loop is missing.");

        var readinessDone = launcher.IndexOf("\ndone\n", readinessLoop, StringComparison.Ordinal);
        Assert.IsTrue(readinessDone > readinessLoop, "The provider listener readiness loop is incomplete.");

        var connectedCallback = launcher.IndexOf(
            "\"/opt/julos-remote-provider/bridge/JulOS.Remote.ProviderBridge\" connected",
            StringComparison.Ordinal);
        Assert.IsTrue(
            connectedCallback > readinessDone,
            "The connected callback must run only after the provider listener readiness loop completes.");
    }
}
