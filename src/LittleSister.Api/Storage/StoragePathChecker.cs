namespace LittleSister.Api.Storage;

/// <summary>Prüft, ob ein Ordner für Aufnahmen angelegt und beschrieben werden kann.</summary>
internal sealed class StoragePathChecker
{
    /// <summary>Liefert <c>null</c>, wenn alles passt, sonst einen Fehlercode für die Oberfläche.</summary>
    public string? Check(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".littlesister-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return "notWritable";
        }
        catch (IOException)
        {
            return "notAccessible";
        }
    }
}
