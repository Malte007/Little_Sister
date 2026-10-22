using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Events;
using LittleSister.Infrastructure.Recording;

namespace LittleSister.Infrastructure.Tests;

public class MotionRecordingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Pre = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Post = TimeSpan.FromSeconds(10);

    // Bewegung 12:00:20 bis 12:00:30
    private static readonly MotionState Ended = new(false, T0.AddSeconds(20), T0.AddSeconds(30));

    [Theory]
    [InlineData(0, false)]  // 12:00:00–05 liegt vor dem Vorlauf
    [InlineData(15, true)]  // 12:00:15–20 ist der Vorlauf
    [InlineData(25, true)]  // während der Bewegung
    [InlineData(35, true)]  // im Nachlauf
    [InlineData(40, false)] // nach dem Nachlauf
    public void Keep_BeruecksichtigtVorUndNachlauf(int startSecond, bool keep)
    {
        var start = T0.AddSeconds(startSecond);
        Assert.Equal(keep, PreRollBuffer.Keep(Ended, start, start.AddSeconds(5), Pre, Post));
    }

    [Fact]
    public void Keep_OhneBewegungNichts() =>
        Assert.False(PreRollBuffer.Keep(MotionState.None, T0, T0.AddSeconds(5), Pre, Post));

    [Fact]
    public async Task Puffer_UebernimmtVorlaufErstBeiBewegungUndLoeschtAltes()
    {
        var dir = Directory.CreateTempSubdirectory("ls-buffer-").FullName;
        try
        {
            var committed = new List<string>();
            var buffer = new PreRollBuffer((file, _) =>
            {
                committed.Add(Path.GetFileName(file));
                return Task.FromResult<Segment?>(new Segment());
            }, Pre, Post);

            string Write(int second)
            {
                var file = Path.Combine(dir, T0.AddSeconds(second).ToString("yyyyMMdd'T'HHmmss'Z'") + ".mp4");
                File.WriteAllText(file, "x");
                return file;
            }

            // Ohne Bewegung bleibt alles im Puffer; was älter als der Vorlauf ist, verschwindet
            var old = Write(0);
            Assert.Null(await buffer.AddAsync(old, Pre, MotionState.None, T0.AddSeconds(5)));
            var recent = Write(5);
            Assert.Null(await buffer.AddAsync(recent, Pre, MotionState.None, T0.AddSeconds(11)));
            Assert.False(File.Exists(old));
            Assert.Empty(committed);

            // Bewegung ab 12:00:12: das Segment 12:00:05–10 ist Vorlauf und wird übernommen
            var moving = new MotionState(true, T0.AddSeconds(12), null);
            await buffer.FlushAsync(moving, T0.AddSeconds(12));
            Assert.Equal([Path.GetFileName(recent)], committed);

            // Neue Segmente während der Bewegung gehen direkt durch
            Assert.NotNull(await buffer.AddAsync(Write(10), Pre, moving, T0.AddSeconds(15)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Signale_MeldenAenderungenUndIgnorierenWiederholungen()
    {
        var signals = new MotionSignals();
        var id = Guid.NewGuid();
        var (state, changed) = signals.Snapshot(id);
        Assert.False(state.Active);

        signals.Set(id, false, T0); // keine Änderung
        Assert.False(changed.IsCompleted);

        signals.Set(id, true, T0);
        await changed.WaitAsync(TimeSpan.FromSeconds(1));
        signals.Set(id, false, T0.AddSeconds(4));

        var now = signals.Snapshot(id).State;
        Assert.Equal(new MotionState(false, T0, T0.AddSeconds(4)), now);
        Assert.True(now.Covers(T0.AddSeconds(6), Post));
        Assert.False(now.Covers(T0.AddSeconds(14), Post));
    }
}
