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

export type CameraState = 'Idle' | 'Connecting' | 'Recording' | 'Error';

export interface CameraStatus {
	state: CameraState;
	sinceUtc: string;
	lastError: string | null;
	restarts: number;
	lastSegmentUtc: string | null;
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
	recordingEnabled: boolean;
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
	events: { timeUtc: string; kind: string }[];
	previousDate: string | null;
	nextDate: string | null;
	/** Abstand der Vorschaubilder in Sekunden */
	thumbnailSeconds: number;
}
