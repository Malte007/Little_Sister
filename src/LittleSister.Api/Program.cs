using LittleSister.Api;
using LittleSister.Api.Endpoints;
using LittleSister.Infrastructure;
using LittleSister.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLittleSisterInfrastructure(builder.Configuration);
builder.Services.AddLittleSisterApi(builder.Configuration);

var app = builder.Build();

// Datenbank migrieren und Rollen anlegen, bevor Anfragen angenommen werden
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Weboberfläche (gebautes SvelteKit) aus wwwroot ausliefern
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

var api = app.MapGroup("/api");
api.MapSetupEndpoints();
api.MapAuthEndpoints();
api.MapUserEndpoints();
api.MapSystemEndpoints();
api.MapCameraEndpoints();
api.MapOnvifEndpoints();
api.MapStorageEndpoints();

// Unbekannte API-Pfade liefern 404 statt der Oberfläche
api.MapFallback(() => Results.NotFound());

// Alle anderen Pfade gehören zur Single-Page-App
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Sichtbar für die Integrationstests (WebApplicationFactory).</summary>
public partial class Program;
