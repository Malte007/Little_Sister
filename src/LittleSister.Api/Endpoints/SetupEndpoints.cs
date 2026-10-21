using LittleSister.Api.Security;
using LittleSister.Api.Storage;
using LittleSister.Core.Security;
using LittleSister.Core.Settings;
using LittleSister.Core.Setup;
using LittleSister.Infrastructure;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LittleSister.Api.Endpoints;

/// <summary>Ersteinrichtung: nur möglich, solange noch kein Benutzer existiert.</summary>
internal static class SetupEndpoints
{
    // Verhindert, dass zwei gleichzeitige Anfragen jeweils einen Admin anlegen
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static void MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/setup").AllowAnonymous().WithTags("Setup");
        group.MapGet("", GetStatus);
        group.MapPost("", CompleteSetup).RequireRateLimiting(Policies.LoginRateLimit);
    }

    internal sealed record SetupStatus(bool Required, string DefaultRecordingsPath, string DefaultTimeZoneId, IReadOnlyList<string> Languages);

    private static async Task<SetupStatus> GetStatus(LittleSisterDbContext db, IOptions<LittleSisterOptions> options) =>
        new(!await db.Users.AnyAsync(),
            options.Value.DefaultRecordingsPath,
            TimeZoneInfo.Local.Id,
            SupportedLanguages.All);

    private static async Task<IResult> CompleteSetup(
        SetupRequest request,
        LittleSisterDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        StoragePathChecker storage,
        TimeProvider time)
    {
        var errors = SetupValidator.Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var storageError = storage.Check(request.RecordingsPath);
        if (storageError is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.RecordingsPath)] = [storageError] });

        await SetupLock.WaitAsync();
        try
        {
            // Nach dem ersten Benutzer ist der Assistent dauerhaft gesperrt
            if (await db.Users.AnyAsync())
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "setupAlreadyCompleted");

            await using var tx = await db.Database.BeginTransactionAsync();

            var admin = new AppUser { UserName = request.UserName.Trim(), CreatedAtUtc = time.GetUtcNow() };
            var created = await users.CreateAsync(admin, request.Password);
            if (!created.Succeeded)
                return created.ToValidationProblem();
            await users.AddToRoleAsync(admin, Roles.Admin);

            db.SystemSettings.Add(new SystemSettings
            {
                Language = request.Language.ToLowerInvariant(),
                TimeZoneId = request.TimeZoneId,
                RecordingsPath = request.RecordingsPath,
                SetupCompletedAtUtc = time.GetUtcNow(),
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // Direkt angemeldet weiter in die Oberfläche
            await signIn.SignInAsync(admin, isPersistent: false);
            return Results.NoContent();
        }
        finally
        {
            SetupLock.Release();
        }
    }
}
