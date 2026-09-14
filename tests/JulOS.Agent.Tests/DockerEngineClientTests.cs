using System.Net;
using System.Text;
using System.Text.Json;

using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class DockerEngineClientTests
{
    [TestMethod]
    public async Task ReadOnlyConfigurationCannotMutateContainer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var client = new DockerEngineClient(
            new DockerEngineOptions(true, DockerEngineOptions.DefaultSocketPath, false),
            httpClient);

        var exception = await Assert.ThrowsExactlyAsync<DockerCommandException>(() => client.ControlAsync(
            JsonSerializer.SerializeToElement(new DockerControlRequest(new string('a', 64), "restart")),
            CancellationToken.None));

        Assert.AreEqual("docker.control_disabled", exception.Code);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ControlUsesOnlyAllowlistedContainerEndpoint()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var client = new DockerEngineClient(
            new DockerEngineOptions(true, DockerEngineOptions.DefaultSocketPath, true),
            httpClient);
        var id = new string('b', 64);

        var result = await client.ControlAsync(
            JsonSerializer.SerializeToElement(new DockerControlRequest(id, "restart")),
            CancellationToken.None);

        Assert.IsTrue(result.GetProperty("succeeded").GetBoolean());
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual($"/containers/{id}/restart?t=10", handler.Requests[0].Path);
    }

    [TestMethod]
    public async Task InventoryIsPagedAndSanitized()
    {
        const string body = """
            [{"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Names":["/app"],"Image":"example:1","ImageID":"sha256:1","State":"running","Status":"Up","Created":1,"Labels":{"com.docker.compose.project":"demo","com.docker.compose.service":"web"},"Ports":[{"PrivatePort":80,"PublicPort":8080,"Type":"tcp","IP":"0.0.0.0"}],"Mounts":[{"Type":"bind","Source":"/secret/host/path","Destination":"/data","RW":true}]}]
            """;
        var handler = new RecordingHandler(request =>
        {
            var responseBody = request.RequestUri!.AbsolutePath.EndsWith("/json", StringComparison.Ordinal)
                && !request.RequestUri.AbsolutePath.EndsWith("containers/json", StringComparison.Ordinal)
                    ? "{\"RestartCount\":2,\"State\":{\"Health\":{\"Status\":\"healthy\"},\"OOMKilled\":false,\"ExitCode\":0,\"Error\":\"\"}}"
                    : body;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var client = new DockerEngineClient(
            new DockerEngineOptions(true, DockerEngineOptions.DefaultSocketPath, false),
            httpClient);

        var result = await client.ReadInventoryAsync(
            JsonSerializer.SerializeToElement(new DockerInventoryReadRequest("containers", 0, 10)),
            CancellationToken.None);

        Assert.AreEqual(1, result.GetProperty("total").GetInt32());
        var container = result.GetProperty("items")[0];
        Assert.AreEqual("demo", container.GetProperty("labels").GetProperty("com.docker.compose.project").GetString());
        Assert.AreEqual("/data", container.GetProperty("mounts")[0].GetProperty("destination").GetString());
        Assert.IsFalse(container.GetProperty("mounts")[0].TryGetProperty("source", out _));
        Assert.AreEqual(2, container.GetProperty("restartCount").GetInt64());
        Assert.AreEqual("healthy", container.GetProperty("health").GetString());
    }

    private sealed record RequestObservation(HttpMethod Method, string Path);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        internal List<RequestObservation> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(new RequestObservation(request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(responseFactory(request));
        }
    }
}
