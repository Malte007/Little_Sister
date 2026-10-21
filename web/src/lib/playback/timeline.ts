// Umrechnung zwischen Uhrzeit und Abspielposition. Die Playlist reiht die Segmente lückenlos
// aneinander; Lücken in der Aufnahme fehlen also im Video, nicht aber auf der Zeitleiste.

export interface TimedSegment {
	id: number;
	startUtc: string;
	endUtc: string;
}

/** Ein Segment mit Uhrzeit (ms seit 1970) und Position im Video (s). */
export interface MapEntry {
	start: number;
	end: number;
	media: number;
}

/** Zeitspanne in ms seit 1970. */
export interface Range {
	start: number;
	end: number;
}

/** Baut die Zuordnung in Playlist-Reihenfolge; die Längen entsprechen den EXTINF-Angaben. */
export function buildMap(segments: TimedSegment[]): MapEntry[] {
	let media = 0;
	return segments.map((s) => {
		const start = Date.parse(s.startUtc);
		const end = Date.parse(s.endUtc);
		const entry = { start, end, media };
		// Wie die Playlist: drei Nachkommastellen, mindestens 1 ms
		media += Math.max(Math.round(end - start), 1) / 1000;
		return entry;
	});
}

/** Gesamtlänge des Videos in Sekunden. */
export function mediaLength(map: MapEntry[]): number {
	const last = map.at(-1);
	return last ? last.media + (last.end - last.start) / 1000 : 0;
}

/** Abspielposition → Uhrzeit; null ohne Segmente. */
export function toWall(map: MapEntry[], mediaTime: number): number | null {
	if (map.length === 0) return null;
	let lo = 0;
	let hi = map.length - 1;
	while (lo < hi) {
		const mid = (lo + hi + 1) >> 1;
		if (map[mid].media <= mediaTime) lo = mid;
		else hi = mid - 1;
	}
	const e = map[lo];
	return Math.min(e.start + Math.max(mediaTime - e.media, 0) * 1000, e.end);
}

/** Uhrzeit → Abspielposition. In einer Lücke geht es mit der nächsten Aufnahme weiter; danach null. */
export function toMedia(map: MapEntry[], wall: number): number | null {
	for (const e of map) {
		if (wall < e.start) return e.media;
		if (wall < e.end) return e.media + (wall - e.start) / 1000;
	}
	return null;
}

/** Fasst aneinanderliegende Segmente für die Anzeige zu Blöcken zusammen (kleine Lücken zählen nicht). */
export function mergeRanges(map: MapEntry[], toleranceMs = 3000): Range[] {
	const ranges: Range[] = [];
	for (const e of map) {
		const last = ranges.at(-1);
		if (last && e.start - last.end <= toleranceMs) last.end = Math.max(last.end, e.end);
		else ranges.push({ start: e.start, end: e.end });
	}
	return ranges;
}

/** Abstand der Beschriftungen auf der Zeitleiste (ms) passend zum sichtbaren Ausschnitt. */
export function tickStep(spanMs: number): number {
	const minute = 60_000;
	const steps = [1, 2, 5, 10, 15, 30, 60, 120, 180, 360].map((m) => m * minute);
	// Etwa acht Beschriftungen über die Breite
	return steps.find((s) => spanMs / s <= 8) ?? steps.at(-1)!;
}

/**
 * Beschriftungen zwischen from und to. Ausgerichtet wird an der Ortszeit, damit z. B. volle Stunden
 * auch bei Zeitzonen mit halben Stunden richtig liegen.
 */
export function ticks(
	from: number,
	to: number,
	step: number,
	offsetMs: (t: number) => number
): number[] {
	const result: number[] = [];
	const local = from + offsetMs(from);
	let t = Math.ceil(local / step) * step - offsetMs(from);
	for (; t <= to; t += step) if (t >= from) result.push(t);
	return result;
}

/** Abstand einer Zeitzone zu UTC in ms zum Zeitpunkt t (z. B. +7200000 für Sommerzeit in Berlin). */
export function zoneOffset(timeZone: string, t: number): number {
	const parts = new Intl.DateTimeFormat('en-US', {
		timeZone,
		hourCycle: 'h23',
		year: 'numeric',
		month: 'numeric',
		day: 'numeric',
		hour: 'numeric',
		minute: 'numeric',
		second: 'numeric'
	}).formatToParts(new Date(t));
	const n = (type: string) => Number(parts.find((p) => p.type === type)?.value);
	const asUtc = Date.UTC(n('year'), n('month') - 1, n('day'), n('hour'), n('minute'), n('second'));
	return asUtc - Math.floor(t / 1000) * 1000;
}
