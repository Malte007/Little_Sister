using System.Security.Claims;
using LittleSister.Api.Security;
using LittleSister.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LittleSister.Api.Endpoints;

/// <summary>Anmelden per Cookie (Weboberfläche) oder Bearer-Token (Apps).</summary>
internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(Policies.LoginRateLimit);
        group.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting(Policies.LoginRateLimit);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization(Policies.Viewer);
        group.MapPost("/password", ChangePassword).RequireAuthorization(Policies.Viewer);
    }

    internal sealed record LoginRequest(string UserName, string Password, bool RememberMe = false);
    internal sealed record RefreshRequest(string RefreshToken);
    internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    internal sealed record CurrentUser(Guid Id, string UserName, IReadOnlyList<string> Roles);

    /// <param name="useTokens">true: Antwort enthält Access-/Refresh-Token statt eines Cookies.</param>
    private static async Task<IResult> Login(LoginRequest request, bool? useTokens, SignInManager<AppUser> signIn)
    {
        signIn.AuthenticationScheme = useTokens == true ? IdentityConstants.BearerScheme : IdentityConstants.ApplicationScheme;

        // lockoutOnFailure: nach 5 Fehlversuchen ist das Konto für 5 Minuten gesperrt
        var result = await signIn.PasswordSignInAsync(request.UserName ?? "", request.Password ?? "", request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "lockedOut");
        if (!result.Succeeded)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "invalidCredentials");

        // Der SignInManager hat Cookie bzw. Token bereits in die Antwort geschrieben
        return Results.Empty;
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        SignInManager<AppUser> signIn,
        IOptionsMonitor<BearerTokenOptions> bearerOptions,
        TimeProvider time)
    {
        var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        // Abgelaufene Tokens oder geänderte Sicherheitsmerkmale (z. B. neues Passwort) werden abgelehnt
        if (ticket?.Properties.ExpiresUtc is not { } expires || time.GetUtcNow() >= expires ||
            await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
            return Results.Challenge();

        return Results.SignIn(await signIn.CreateUserPrincipalAsync(user), authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> Logout(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
            return Results.Unauthorized();
        return Results.Ok(new CurrentUser(user.Id, user.UserName!, [.. await users.GetRolesAsync(user)]));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
            return Results.Unauthorized();

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword ?? "", request.NewPassword ?? "");
        if (!result.Succeeded)
            return result.ToValidationProblem();

        // Neues Cookie, da sich der Sicherheitsstempel geändert hat
        await signIn.RefreshSignInAsync(user);
        return Results.NoContent();
    }
}
