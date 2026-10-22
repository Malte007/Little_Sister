// Schlanker Zugriff auf die Little-Sister-API; Anmeldung läuft über das HttpOnly-Cookie.

/** Fehlerantwort der API (ProblemDetails bzw. ValidationProblem). */
export class ApiError extends Error {
	constructor(
		public readonly status: number,
		/** Fehlercode aus "title", z. B. "invalidCredentials". */
		public readonly code: string,
		/** Feldfehler, z. B. { password: ['PasswordTooShort'] }. */
		public readonly fields: Record<string, string[]> = {}
	) {
		super(code);
	}

	/** Liefert den ersten Fehlercode für ein Feld (Groß-/Kleinschreibung egal). */
	field(name: string): string | undefined {
		const key = Object.keys(this.fields).find((k) => k.toLowerCase() === name.toLowerCase());
		return key ? this.fields[key][0] : undefined;
	}
}

/** Baut aus einer fehlgeschlagenen Antwort einen ApiError. */
export async function toApiError(response: Response): Promise<ApiError> {
	let body: { title?: string; errors?: Record<string, string[]> } = {};
	try {
		body = await response.json();
	} catch {
		// Kein JSON, z. B. bei 429 – dann reicht der Statuscode
	}
	return new ApiError(response.status, body.title ?? `http${response.status}`, body.errors ?? {});
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

/** Ruft einen API-Endpunkt auf und liefert das JSON-Ergebnis (oder undefined bei 204). */
export async function api<T = void>(
	path: string,
	options: { method?: Method; body?: unknown; fetch?: typeof fetch } = {}
): Promise<T> {
	const response = await (options.fetch ?? fetch)(path, {
		method: options.method ?? 'GET',
		headers: options.body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
		body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
		credentials: 'same-origin'
	});

	if (!response.ok) throw await toApiError(response);
	if (response.status === 204 || response.headers.get('content-length') === '0')
		return undefined as T;

	const text = await response.text();
	return (text ? JSON.parse(text) : undefined) as T;
}

/** Stream-Adresse samt Zugangsdaten für Prüfung und Standbild; ohne Passwort gilt das gespeicherte der Kamera. */
export interface StreamTarget {
	url: string;
	username: string | null;
	password: string | null;
	cameraId: string | null;
}

/** Holt ein Standbild direkt vom Stream (auch für noch nicht gespeicherte Kameras). */
export async function streamSnapshot(target: StreamTarget): Promise<Blob> {
	const response = await fetch('/api/cameras/snapshot', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(target),
		credentials: 'same-origin'
	});
	if (!response.ok) throw await toApiError(response);
	return response.blob();
}

/** Datentypen der API */
export interface SetupStatus {
	required: boolean;
	defaultRecordingsPath: string;
	defaultTimeZoneId: string;
	languages: string[];
}

export interface CurrentUser {
	id: string;
	userName: string;
	roles: string[];
}

export interface UserInfo {
	id: string;
	userName: string;
	role: 'Admin' | 'Viewer';
	lockedOut: boolean;
	createdAtUtc: string;
}

export interface SystemInfo {
	version: string;
	language: string;
	timeZoneId: string;
}

/** Standby = wartet auf Bewegung, speichert nichts */
export type CameraState = 'Idle' | 'Standby' | 'Connecting' | 'Recording' | 'Error';

/** Off = keine Aufnahme, Continuous = rund um die Uhr, Motion = nur bei Bewegung */
export type RecordingMode = 'Off' | 'Continuous' | 'Motion';

/** Video = Little Sister erkennt Bewegung im Bild, Onvif = die Kamera meldet sie */
export type MotionDetection = 'Video' | 'Onvif';

export interface CameraStatus {
	state: CameraState;
	sinceUtc: string;
	lastError: string | null;
	restarts: number;
	lastSegmentUtc: string | null;
	/** Verbindung zur Bewegungserkennung (Bild oder ONVIF) */
	events: 'None' | 'Connecting' | 'Connected' | 'Error';
	eventError: string | null;
	/** Läuft gerade eine Bewegung? */
	motion: boolean;
	lastMotionUtc: string | null;
}

/** Ob die Live-Ansicht bereitsteht; error z. B. "go2rtcMissing". */
export interface LiveState {
	available: boolean;
	error: string | null;
}

export interface Camera {
	id: string;
	name: string;
	mainStreamUrl: string;
	subStreamUrl: string | null;
	username: string | null;
	hasPassword: boolean;
	onvifAddress: string | null;
	manufacturer: string | null;
	model: string | null;
	recordingEnabled: boolean;
	recordingMode: RecordingMode;
	/** Sekunden vor bzw. nach einer Bewegung, die mit aufgehoben werden */
	preRollSeconds: number;
	postRollSeconds: number;
	motionDetection: MotionDetection;
	/** Empfindlichkeit der Bilderkennung, 1–100 */
	motionSensitivity: number;
	/** Erkennungsbereiche: 16×12 Felder '0'/'1', zeilenweise; null = ganzes Bild */
	motionZones: string | null;
	quotaGb: number;
	maxRetentionDays: number | null;
	/** Drehung im Uhrzeigersinn: 0, 90, 180 oder 270 */
	rotation: number;
	status: CameraStatus;
	storage: {
		usedBytes: number;
		segmentCount: number;
		oldestUtc: string | null;
		newestUtc: string | null;
		estimatedDays: number | null;
	};
}

/** Eingaben beim Anlegen/Ändern; password null = beim Ändern unverändert lassen. */
export interface CameraInput {
	name: string;
	mainStreamUrl: string;
	subStreamUrl: string | null;
	username: string | null;
	password: string | null;
	onvifAddress: string | null;
	manufacturer: string | null;
	model: string | null;
	recordingMode: RecordingMode;
	preRollSeconds: number;
	postRollSeconds: number;
	motionDetection: MotionDetection;
	motionSensitivity: number;
	motionZones: string | null;
	quotaGb: number;
	maxRetentionDays: number | null;
	rotation: number;
}

export interface DiscoveredDevice {
	address: string;
	host: string;
	name: string | null;
	hardware: string | null;
}

export interface OnvifProfile {
	token: string;
	name: string;
	encoding: string | null;
	width: number;
	height: number;
	streamUri: string | null;
}

export interface OnvifProbeResult {
	device: {
		manufacturer: string | null;
		model: string | null;
		firmwareVersion: string | null;
		serialNumber: string | null;
	};
	profiles: OnvifProfile[];
	suggestedMainStream: string | null;
	suggestedSubStream: string | null;
}

export interface StreamProbeResult {
	success: boolean;
	codec: string | null;
	width: number;
	height: number;
	error: string | null;
}

export interface StorageInfo {
	recordingsPath: string;
	freeBytes: number | null;
	totalBytes: number | null;
	usedBytes: number;
	minFreeGb: number;
	/** Vorschaubilder für die Zeitleiste erzeugen */
	thumbnailsEnabled: boolean;
}

/** Aufnahmen eines Tages in der Zeitzone der Installation (Zeitleiste und Wiedergabe). */
export interface RecordingDay {
	/** Tag als JJJJ-MM-TT */
	date: string;
	today: string;
	timeZoneId: string;
	fromUtc: string;
	toUtc: string;
	/** Abspielbare Segmente in Playlist-Reihenfolge; thumbnails = Bilder im Bildstreifen */
	segments: { id: number; startUtc: string; endUtc: string; thumbnails: number }[];
	/** Ereignisse wie Bewegung; endUtc null = läuft noch */
	events: { startUtc: string; endUtc: string | null; kind: string }[];
	previousDate: string | null;
	nextDate: string | null;
	/** Abstand der Vorschaubilder in Sekunden; 0 = vom Admin ausgeschaltet */
	thumbnailSeconds: number;
}
