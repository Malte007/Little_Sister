using System.Security.Claims;
using LittleSister.Api.Security;
using LittleSister.Core.Security;
using LittleSister.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Api.Endpoints;

/// <summary>Benutzerverwaltung, nur für Admins.</summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").RequireAuthorization(Policies.Admin).WithTags("Users");
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapDelete("/{id:guid}", Delete);
    }

    internal sealed record UserDto(Guid Id, string UserName, string Role, bool LockedOut, DateTimeOffset CreatedAtUtc);
    internal sealed record CreateUserRequest(string UserName, string Password, string Role);

    private static async Task<IReadOnlyList<UserDto>> List(UserManager<AppUser> users, TimeProvider time)
    {
        var all = await users.Users.OrderBy(u => u.UserName).ToListAsync();
        var result = new List<UserDto>(all.Count);
        foreach (var u in all)
        {
            var role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? "";
            result.Add(new UserDto(u.Id, u.UserName!, role, u.LockoutEnd > time.GetUtcNow(), u.CreatedAtUtc));
        }
        return result;
    }

    private static async Task<IResult> Create(CreateUserRequest request, UserManager<AppUser> users, TimeProvider time)
    {
        if (!Roles.TryNormalize(request.Role, out var role))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["unknown"] });
        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["required"] });

        var user = new AppUser { UserName = request.UserName.Trim(), CreatedAtUtc = time.GetUtcNow() };
        var created = await users.CreateAsync(user, request.Password ?? "");
        if (!created.Succeeded)
            return created.ToValidationProblem();

        await users.AddToRoleAsync(user, role);
        return Results.Created($"/api/users/{user.Id}", new UserDto(user.Id, user.UserName, role, false, user.CreatedAtUtc));
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        // Das eigene Konto zu löschen würde im Extremfall den letzten Admin entfernen
        if (users.GetUserId(principal) == id.ToString())
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "cannotDeleteSelf");

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();

        await users.DeleteAsync(user);
        return Results.NoContent();
    }
}
