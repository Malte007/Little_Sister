import { describe, expect, it } from 'vitest';
import { formatBytes, formatDays } from './format';

describe('formatBytes', () => {
	it('wählt die passende Einheit', () => {
		expect(formatBytes(512, 'de')).toBe('512 B');
		expect(formatBytes(1536, 'de')).toBe('1,5 KB');
		expect(formatBytes(200 * 1024 ** 3, 'en')).toBe('200 GB');
	});

	it('zeigt einen Strich ohne Wert', () => {
		expect(formatBytes(null, 'de')).toBe('–');
	});
});

describe('formatDays', () => {
	it('rundet auf ganze Tage', () => {
		expect(formatDays(4.6, 'de')).toBe('5');
		expect(formatDays(0.5, 'de')).toBe('0,5');
		expect(formatDays(null, 'de')).toBeNull();
	});
});
