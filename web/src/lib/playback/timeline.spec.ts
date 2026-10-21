import { describe, expect, it } from 'vitest';
import {
	buildMap,
	eventNeighbor,
	mediaLength,
	mergeRanges,
	tickStep,
	ticks,
	toMedia,
	toWall,
	zoneOffset
} from './timeline';

const t0 = Date.parse('2026-10-21T10:00:00Z');
const iso = (ms: number) => new Date(ms).toISOString();
// Zwei Minuten am Stück, dann zehn Minuten Lücke, dann eine Minute
const segments = [
	{ id: 1, startUtc: iso(t0), endUtc: iso(t0 + 60_000) },
	{ id: 2, startUtc: iso(t0 + 60_000), endUtc: iso(t0 + 120_000) },
	{ id: 3, startUtc: iso(t0 + 720_000), endUtc: iso(t0 + 780_000) }
];

describe('Zeitleiste', () => {
	const map = buildMap(segments);

	it('reiht Segmente ohne Lücken aneinander', () => {
		expect(map.map((e) => e.media)).toEqual([0, 60, 120]);
		expect(mediaLength(map)).toBe(180);
	});

	it('rechnet in beide Richtungen um', () => {
		expect(toWall(map, 90)).toBe(t0 + 90_000);
		expect(toWall(map, 130)).toBe(t0 + 730_000);
		expect(toMedia(map, t0 + 730_000)).toBe(130);
	});

	it('springt aus einer Lücke zur nächsten Aufnahme', () => {
		expect(toMedia(map, t0 + 300_000)).toBe(120);
		expect(toMedia(map, t0 - 1)).toBe(0);
		expect(toMedia(map, t0 + 900_000)).toBeNull();
		expect(toWall([], 0)).toBeNull();
	});

	it('fasst Blöcke für die Anzeige zusammen', () => {
		expect(mergeRanges(map)).toEqual([
			{ start: t0, end: t0 + 120_000 },
			{ start: t0 + 720_000, end: t0 + 780_000 }
		]);
	});

	it('beschriftet volle Stunden in Ortszeit', () => {
		const from = Date.parse('2026-10-20T22:00:00Z'); // Mitternacht in Berlin
		const day = 24 * 3_600_000;
		expect(tickStep(day)).toBe(3 * 3_600_000);
		const labels = ticks(from, from + day, 3 * 3_600_000, (t) => zoneOffset('Europe/Berlin', t));
		expect(labels[0]).toBe(from);
		expect(labels).toHaveLength(9);
		expect(zoneOffset('Asia/Kolkata', from)).toBe(5.5 * 3_600_000);
	});

	it('springt zum vorherigen und nächsten Ereignis', () => {
		const starts = [10_000, 50_000, 90_000];
		expect(eventNeighbor(starts, 50_000, 1)).toBe(90_000);
		expect(eventNeighbor(starts, 50_000, -1)).toBe(10_000);
		// Kurz nach einem Sprung zurück: nicht am selben Ereignis hängen bleiben
		expect(eventNeighbor(starts, 51_000, -1)).toBe(10_000);
		expect(eventNeighbor(starts, 95_000, 1)).toBeNull();
		expect(eventNeighbor([], 0, -1)).toBeNull();
	});
});
