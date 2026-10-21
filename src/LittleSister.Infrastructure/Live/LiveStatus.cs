namespace LittleSister.Infrastructure.Live;

/// <summary>Ob die Live-Ansicht gerade bereitsteht; Fehler als Code für die Oberfläche.</summary>
public sealed record LiveState(bool Available, string? Error = null);

public sealed class LiveStatus
{
    private volatile LiveState state = new(false, "starting");

    public LiveState Current => state;

    public void Set(LiveState value) => state = value;
}
