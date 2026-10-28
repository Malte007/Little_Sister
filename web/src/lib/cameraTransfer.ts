// Kamera-Konfigurationen als Datei exportieren und wieder importieren
import { api, ApiError } from './api.js';

/** Ergebnis je importierter Kamera. */
export interface ImportedCamera {
	id: string;
	name: string;
	/** Benutzername gesetzt, aber kein Passwort in der Datei: muss noch eingetragen werden. */
	needsPassword: boolean;
}

/** Lädt die Exportdatei vom Server und bietet sie dem Browser zum Speichern an. */
export async function exportCameras(options: {
	ids?: string[];
	includePasswords: boolean;
	fileName: string;
}) {
	const query = new URLSearchParams();
	for (const id of options.ids ?? []) query.append('id', id);
	if (options.includePasswords) query.set('includePasswords', 'true');
	const file = await api<unknown>(`/api/cameras/export?${query}`);

	// Download über einen unsichtbaren Link; funktioniert auch über http://<IP>
	const url = URL.createObjectURL(
		new Blob([JSON.stringify(file, null, 2)], { type: 'application/json' })
	);
	const link = document.createElement('a');
	link.href = url;
	link.download = options.fileName;
	link.click();
	setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Liest eine Exportdatei ein und legt ihre Kameras an; wirft ApiError mit Code "importInvalidFile" bei kaputtem JSON. */
export async function importCameras(file: File): Promise<ImportedCamera[]> {
	let body: unknown;
	try {
		body = JSON.parse(await file.text());
	} catch {
		throw new ApiError(400, 'importInvalidFile');
	}
	return api<ImportedCamera[]>('/api/cameras/import', { method: 'POST', body });
}

/** Dateiname mit Datum, z. B. "little-sister-garten-2026-10-28.json". */
export function exportFileName(name: string): string {
	const slug =
		name
			.toLowerCase()
			.replace(/[^a-z0-9äöüß]+/g, '-')
			.replace(/^-|-$/g, '') || 'kameras';
	return `little-sister-${slug}-${new Date().toISOString().slice(0, 10)}.json`;
}
