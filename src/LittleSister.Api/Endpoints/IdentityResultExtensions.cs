using Microsoft.AspNetCore.Identity;

namespace LittleSister.Api.Endpoints;

internal static class IdentityResultExtensions
{
    /// <summary>Wandelt Identity-Fehler (z. B. Passwort zu kurz) in eine ValidationProblem-Antwort um.</summary>
    public static IResult ToValidationProblem(this IdentityResult result) =>
        Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "userName")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()));
}
