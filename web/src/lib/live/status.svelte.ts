// Kamerastatus live per SignalR; ohne Verbindung gelten die zuletzt geladenen Daten.
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { Camera, CameraStatus } from '#lib/api.js';

const statuses = $state<Record<string, CameraStatus | null>>({});
let connection: HubConnection | null = null;

/** Verbindet sich mit dem Status-Hub (nach dem Login aufrufen); erneute Aufrufe schaden nicht. */
export function connectStatus() {
	if (connection) return;
	connection = new HubConnectionBuilder()
		.withUrl('/api/hubs/status')
		.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
		.configureLogging(LogLevel.Warning)
		.build();
	connection.on('status', (id: string, status: CameraStatus | null) => {
		statuses[id] = status;
	});
	connection.start().catch(() => {
		// Erster Verbindungsaufbau fehlgeschlagen: später erneut versuchen
		connection = null;
		setTimeout(connectStatus, 5000);
	});
}

/** Trennt die Verbindung, z. B. beim Abmelden. */
export function disconnectStatus() {
	connection?.stop();
	connection = null;
}

/** Aktuellster bekannter Status einer Kamera. */
export function statusOf(camera: Camera): CameraStatus {
	const live = statuses[camera.id];
	if (!live) return camera.status;
	// Ältere Meldungen als die geladenen Daten ignorieren
	return live.sinceUtc >= camera.status.sinceUtc ? live : camera.status;
}
