// Mehrere Kameras synchron abspielen. Die Uhrzeit gibt die "führende" Kamera vor: die erste der
// Auswahl, die gerade eine Aufnahme hat. Die anderen folgen ihr; wer gerade keine Aufnahme hat, pausiert.
import { nextStart, segmentAt, type MapEntry } from './timeline';

/** Höchstens so viele Kameras gleichzeitig; jede dekodiert ein eigenes Video im Browser. */
export const maxCameras = 9;

/** Index der führenden Kamera zur Uhrzeit; -1, wenn keine Kamera eine Aufnahme hat. */
export function leaderAt(maps: MapEntry[][], wall: number): number {
	return maps.findIndex((m) => segmentAt(m, wall) >= 0);
}

/** Früheste Uhrzeit ab wall, zu der eine der Kameras eine Aufnahme hat; null, wenn keine mehr folgt. */
export function nextRecording(maps: MapEntry[][], wall: number, skip = -1): number | null {
	let best: number | null = null;
	maps.forEach((m, i) => {
		if (i === skip) return;
		const t = segmentAt(m, wall) >= 0 ? wall : nextStart(m, wall);
		if (t !== null && (best === null || t < best)) best = t;
	});
	return best;
}

/**
 * Sprungziele der Bewegungsereignisse: dort, wo die Wiedergabe nach dem Sprung tatsächlich steht.
 * Ohne Vorlauf beginnt die Aufnahme erst Sekunden nach dem Ereignis; ein Sprung landet dann am Aufnahmebeginn.
 * Mit den Ereignisstarts selbst fände "vorheriges Ereignis" wieder dasselbe und käme nie weiter zurück.
 */
export function eventTargets(maps: MapEntry[][], starts: number[]): number[] {
	const targets = starts.map((s) => nextRecording(maps, s)).filter((t): t is number => t !== null);
	return [...new Set(targets)].sort((x, y) => x - y);
}

/**
 * Die führende Kamera ist von prev nach next gesprungen, weil ihre Aufnahme eine Lücke hat
 * (ihr Video reiht die Aufnahmen lückenlos aneinander). Haben andere Kameras in der Lücke
 * Aufnahmen, geht es dort weiter, sonst bei next.
 */
export function settleJump(maps: MapEntry[][], leader: number, prev: number, next: number): number {
	const i = segmentAt(maps[leader], prev);
	const gapStart = i >= 0 ? maps[leader][i].end : prev;
	const other = nextRecording(maps, gapStart, leader);
	return other !== null && other < next ? other : next;
}

/**
 * Zielstellen beim Angleichen: Abspielposition je Kamera, null = pausieren (Lücke), undefined = in Ruhe lassen.
 * Die führende Kamera gibt die Zeit vor und wird nicht an sich selbst angeglichen: Überlappen sich zwei
 * Segmente auf der Uhr (Dateiname auf Sekunden gerundet, Kamerauhr weicht ab, Pufferung beim Verbinden),
 * gehören zu einer Uhrzeit zwei Videostellen, und das Zurückrechnen würde sie in eine Endlosschleife werfen.
 */
export function followTargets(
	maps: MapEntry[][],
	wall: number,
	previousLeader: number,
	leader: number,
	force: boolean
): (number | null | undefined)[] {
	const keepLeader = !force && leader === previousLeader;
	return maps.map((m, i) => (keepLeader && i === leader ? undefined : mediaAt(m, wall)));
}

/** Abspielposition einer Kamera zur Uhrzeit; null in einer Lücke (die Kamera pausiert dann). */
export function mediaAt(map: MapEntry[], wall: number): number | null {
	const i = segmentAt(map, wall);
	return i < 0 ? null : map[i].media + (wall - map[i].start) / 1000;
}

/** Ab welchem Sprung (ms Uhrzeit) eine Lücke vorliegt; bei schnellem Abspielen liegen Zeitmeldungen weiter auseinander. */
export function jumpTolerance(rate: number): number {
	return 2000 + rate * 1000;
}

/** Bild aus dem Bildstreifen eines Segments zur Uhrzeit. */
export interface Thumbnail {
	src: string;
	index: number;
	count: number;
}

export function thumbnailAt(map: MapEntry[], wall: number, seconds: number): Thumbnail | null {
	const i = segmentAt(map, wall);
	if (i < 0) return null;
	const e = map[i];
	const index = Math.min(Math.floor((wall - e.start) / (seconds * 1000)), e.thumbnails - 1);
	return { src: `/api/recordings/segments/${e.id}/thumbnails.jpg`, index, count: e.thumbnails };
}
