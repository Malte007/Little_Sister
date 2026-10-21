// Mehrsprachigkeit: alle Texte liegen in den JSON-Dateien, nichts steht fest im Code.
import de from './de.json';
import en from './en.json';
import { matchLanguage, translate, type Dictionary } from './translate';

const dictionaries: Record<string, Dictionary> = { de, en };
export const languages = Object.keys(dictionaries);
const fallback = 'de';
const storageKey = 'littlesister.language';

/** Gewählte Sprache des Browsers merken (pro Gerät), sonst Browsersprache. */
function initialLanguage(): string {
	try {
		const stored = localStorage.getItem(storageKey);
		if (stored && languages.includes(stored)) return stored;
	} catch {
		// localStorage kann gesperrt sein (privater Modus)
	}
	return matchLanguage(globalThis.navigator?.language, languages, fallback);
}

const state = $state({ language: initialLanguage(), chosen: false });

export const i18n = {
	get language() {
		return state.language;
	},
	/** Vom Benutzer gewählte Sprache; wird gespeichert. */
	set(language: string) {
		if (!languages.includes(language)) return;
		state.language = language;
		state.chosen = true;
		try {
			localStorage.setItem(storageKey, language);
		} catch {
			// Ohne Speicher gilt die Wahl nur bis zum Neuladen
		}
		document.documentElement.lang = language;
	},
	/** Sprache der Installation übernehmen, sofern der Benutzer nichts gewählt hat. */
	useDefault(language: string) {
		if (!state.chosen && languages.includes(language)) {
			state.language = language;
			document.documentElement.lang = language;
		}
	}
};

/** Übersetzt einen Schlüssel; fällt auf Deutsch und zuletzt auf den Schlüssel selbst zurück. */
export function t(key: string, params?: Record<string, string | number>): string {
	return (
		translate(dictionaries[state.language], key, params) ??
		translate(dictionaries[fallback], key, params) ??
		key
	);
}

/** Übersetzt einen Fehlercode der API; unbekannte Codes ergeben eine allgemeine Meldung. */
export function tError(code: string | undefined): string {
	if (!code) return t('errors.generic');
	const text = t(`errors.${code}`);
	return text === `errors.${code}` ? t('errors.generic') : text;
}
