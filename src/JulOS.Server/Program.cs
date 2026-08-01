// JulOS Server composition root.
// Middleware, authentication, persistence and endpoints are wired by later work items.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
