using System.Threading.Channels;
using LittleSister.Api.Endpoints;
using LittleSister.Infrastructure.Recording;
using Microsoft.AspNetCore.SignalR;

namespace LittleSister.Api.Live;

/// <summary>
/// Meldet Statusänderungen der Kameras sofort an alle angemeldeten Oberflächen.
/// Nachricht "status": Kamera-Id und neuer Status (null = Kamera gelöscht).
/// </summary>
internal sealed class StatusHub : Hub;

/// <summary>Leitet Änderungen aus der <see cref="CameraStatusRegistry"/> der Reihe nach an den Hub weiter.</summary>
internal sealed class StatusBroadcaster(CameraStatusRegistry statuses, IHubContext<StatusHub> hub, ILogger<StatusBroadcaster> logger)
    : BackgroundService
{
    private readonly Channel<(Guid Id, CameraStatus? Status)> queue = Channel.CreateUnbounded<(Guid, CameraStatus?)>();

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        statuses.Changed += OnChanged;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        statuses.Changed -= OnChanged;
        return base.StopAsync(cancellationToken);
    }

    private void OnChanged(Guid id, CameraStatus? status) => queue.Writer.TryWrite((id, status));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (id, status) in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await hub.Clients.All.SendAsync("status", id, status is null ? null : CameraEndpoints.CameraStatusDto.From(status), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Statusmeldung für {Camera} nicht gesendet", id);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
