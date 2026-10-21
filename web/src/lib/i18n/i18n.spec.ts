import { describe, expect, it } from 'vitest';
import de from './de.json';
import en from './en.json';
import type { Dictionary } from './translate';

/** Sammelt alle Schlüssel als Punkt-Pfade. */
function keys(dict: Dictionary, prefix = ''): string[] {
	return Object.entries(dict).flatMap(([k, v]) =>
		typeof v === 'string' ? [prefix + k] : keys(v, `${prefix}${k}.`)
	);
}

describe('Sprachdateien', () => {
	it('Deutsch und Englisch enthalten dieselben Schlüssel', () => {
		expect(keys(en).sort()).toEqual(keys(de).sort());
	});
});
