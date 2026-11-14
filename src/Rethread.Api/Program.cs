using Rethread.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddRethread();

var app = builder.Build();

if (args is ["seed", ..])
{
    return await app.RunSeedCommandAsync(args);
}

app.UseRethread();
app.MapRethread();
await app.PrepareDatabaseAsync();
await app.RunAsync();
return 0;

/// <summary>Visible to <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
