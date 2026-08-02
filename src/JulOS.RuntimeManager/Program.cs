using JulOS.RuntimeManager;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
var options = RuntimeManagerOptions.Read(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new RuntimePolicy(options.AllowedNetworks));
builder.Services.AddSingleton<IRuntimeBackend, DockerCliRuntimeBackend>();

var app = builder.Build();
app.UseMiddleware<RuntimeManagerAuthenticationMiddleware>();
app.MapRuntimeManager();
await app.RunAsync().ConfigureAwait(false);
