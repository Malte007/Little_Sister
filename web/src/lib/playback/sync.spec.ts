import { describe, expect, it } from 'vitest';
import { buildMap, eventNeighbor, toWall } from './timeline';
import {
	eventTargets,
	followTargets,
	leaderAt,
	mediaAt,
	nextRecording,
	settleJump,
	thumbnailAt
} from './sync';

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

	it('springt bei Aufnahme ohne Vorlauf von Ereignis zu Ereignis zurück', () => {
		// Bewegung um 10 s und 50 s, die Aufnahme beginnt jeweils erst 4 s später
		const sec = 1000;
		const at = (id: number, from: number, to: number) => ({
			id,
			startUtc: new Date(t0 + from * sec).toISOString(),
			endUtc: new Date(t0 + to * sec).toISOString(),
			thumbnails: 0
		});
		const late = [buildMap([at(1, 14, 40), at(2, 54, 80)])];
		const targets = eventTargets(late, [t0 + 10 * sec, t0 + 50 * sec]);
		expect(targets).toEqual([t0 + 14 * sec, t0 + 54 * sec]);
		// Nach dem Sprung steht die Wiedergabe am Aufnahmebeginn; zurück führt zum Ereignis davor
		expect(eventNeighbor(targets, t0 + 54 * sec, -1)).toBe(t0 + 14 * sec);
		expect(eventNeighbor(targets, t0 + 14 * sec, -1)).toBeNull();
		expect(eventNeighbor(targets, t0 + 14 * sec, 1)).toBe(t0 + 54 * sec);
	});

	it('überlappende Segmente: Uhrzeit ist nicht eindeutig, die führende Kamera darf nicht zurückgerechnet werden', () => {
		// Zweites Segment beginnt laut Dateiname 2 s vor dem Ende des ersten
		const sec = 1000;
		const at = (id: number, from: number, to: number) => ({
			id,
			startUtc: new Date(t0 + from * sec).toISOString(),
			endUtc: new Date(t0 + to * sec).toISOString(),
			thumbnails: 0
		});
		const map = buildMap([at(1, 0, 60), at(2, 58, 118)]);
		// Videostelle 61 s (im zweiten Segment) entspricht 59 s Uhrzeit ...
		const wall = toWall(map, 61)!;
		expect(wall).toBe(t0 + 59 * sec);
		// ... und 59 s Uhrzeit zurückgerechnet ergibt eine andere Videostelle: Grund der früheren Schleife
		expect(mediaAt(map, wall)).not.toBe(61);
	});

	it('läuft über überlappende Segmente hinweg weiter statt in einer Schleife', () => {
		// Wie auf Maltes Server: das erste Segment nach dem Start von ffmpeg ragt 2,9 s in das nächste
		const sec = 1000;
		const at = (id: number, from: number, to: number) => ({
			id,
			startUtc: new Date(t0 + from * sec).toISOString(),
			endUtc: new Date(t0 + to * sec).toISOString(),
			thumbnails: 0
		});
		const one = [buildMap([at(1, -10, 0), at(2, 0, 10.9), at(3, 8, 18), at(4, 18, 28)])];
		// Abspielen in Schritten von 0,25 s wie die timeupdate-Meldungen, Abweichung > 1 s führt zum Sprung
		let media = 16;
		let leader = 0;
		for (let step = 0; step < 80; step++) {
			media += 0.25;
			const wall = toWall(one[0], media)!;
			const next = leaderAt(one, wall);
			const target = followTargets(one, wall, leader, next, false)[0];
			leader = next;
			if (target != null && Math.abs(media - target) > 1) media = target;
		}
		// 20 s Abspielzeit: ohne Schleife bei 36 s Videostelle
		expect(media).toBeCloseTo(36, 5);
	});
});
