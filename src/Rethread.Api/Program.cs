using Rethread.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddRethread();

var app = builder.Build();
app.UseRethread();
app.MapRethread();
await app.MigrateIfConfiguredAsync();
await app.RunAsync();

/// <summary>Visible to <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
