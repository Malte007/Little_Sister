namespace LittleSister.Core.Setup;

/// <summary>Eingaben des Einrichtungsassistenten beim ersten Start.</summary>
public sealed record SetupRequest(
    string UserName,
    string Password,
    string Language,
    string TimeZoneId,
    string RecordingsPath);
