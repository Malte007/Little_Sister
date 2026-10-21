import { describe, expect, it } from 'vitest';
import { buildMap } from './timeline';
import { leaderAt, mediaAt, nextRecording, settleJump, thumbnailAt } from './sync';

const t0 = Date.parse('2026-10-21T10:00:00Z');
const min = 60_000;
const seg = (id: number, from: number, to: number) => ({
	id,
	startUtc: new Date(t0 + from * min).toISOString(),
	endUtc: new Date(t0 + to * min).toISOString(),
	thumbnails: 6
});

// Kamera A: 0–2 und 10–11 Minuten, Kamera B: 1–5 Minuten
const a = buildMap([seg(1, 0, 1), seg(2, 1, 2), seg(3, 10, 11)]);
const b = buildMap([seg(4, 1, 5)]);
const maps = [a, b];

describe('Synchronlauf', () => {
	it('führt mit der ersten Kamera, die eine Aufnahme hat', () => {
		expect(leaderAt(maps, t0 + 1.5 * min)).toBe(0);
		expect(leaderAt(maps, t0 + 3 * min)).toBe(1);
		expect(leaderAt(maps, t0 + 7 * min)).toBe(-1);
	});

	it('findet die nächste Aufnahme über alle Kameras', () => {
		expect(nextRecording(maps, t0 + 3 * min)).toBe(t0 + 3 * min);
		expect(nextRecording(maps, t0 + 7 * min)).toBe(t0 + 10 * min);
		expect(nextRecording(maps, t0 + 12 * min)).toBeNull();
	});

	it('springt über eine Lücke nur, wenn keine andere Kamera dazwischen aufnimmt', () => {
		// A springt von 1:59 auf 10:00, B nimmt ab 2:00 weiter auf
		expect(settleJump(maps, 0, t0 + 1.98 * min, t0 + 10 * min)).toBe(t0 + 2 * min);
		// Nur A: der Sprung bleibt
		expect(settleJump([a], 0, t0 + 1.98 * min, t0 + 10 * min)).toBe(t0 + 10 * min);
	});

	it('rechnet die Abspielposition je Kamera um und pausiert in Lücken', () => {
		expect(mediaAt(a, t0 + 10.5 * min)).toBe(150);
		expect(mediaAt(b, t0 + 2 * min)).toBe(60);
		expect(mediaAt(b, t0 + 6 * min)).toBeNull();
	});

	it('wählt das passende Vorschaubild', () => {
		expect(thumbnailAt(a, t0 + 1 * min + 25_000, 10)).toEqual({
			src: '/api/recordings/segments/2/thumbnails.jpg',
			index: 2,
			count: 6
		});
		expect(thumbnailAt(a, t0 + 5 * min, 10)).toBeNull();
	});
});
