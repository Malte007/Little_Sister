import { describe, expect, it } from 'vitest';
import { matchLanguage, translate } from './translate';

const dict = { login: { title: 'Anmelden', hello: 'Hallo {name}' } };

describe('translate', () => {
	it('findet verschachtelte Schlüssel', () => {
		expect(translate(dict, 'login.title')).toBe('Anmelden');
	});

	it('setzt Platzhalter ein', () => {
		expect(translate(dict, 'login.hello', { name: 'Malte' })).toBe('Hallo Malte');
	});

	it('liefert undefined für unbekannte Schlüssel', () => {
		expect(translate(dict, 'login.fehlt')).toBeUndefined();
		expect(translate(dict, 'login')).toBeUndefined();
	});
});

describe('matchLanguage', () => {
	it('erkennt Regionalvarianten', () => {
		expect(matchLanguage('de-AT', ['de', 'en'], 'en')).toBe('de');
	});

	it('fällt auf den Standard zurück', () => {
		expect(matchLanguage('fr-FR', ['de', 'en'], 'de')).toBe('de');
		expect(matchLanguage(undefined, ['de', 'en'], 'en')).toBe('en');
	});
});
