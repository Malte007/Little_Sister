// Reine Übersetzungslogik ohne Svelte-Abhängigkeit, damit sie einfach testbar ist.

export type Dictionary = { [key: string]: string | Dictionary };

/** Sucht einen Text über einen Punkt-Pfad ("login.title") und setzt {platzhalter} ein. */
export function translate(
	dictionary: Dictionary,
	key: string,
	params: Record<string, string | number> = {}
): string | undefined {
	let node: string | Dictionary | undefined = dictionary;
	for (const part of key.split('.')) {
		node = typeof node === 'object' ? node[part] : undefined;
	}
	if (typeof node !== 'string') return undefined;
	return node.replace(/\{(\w+)\}/g, (match, name: string) => String(params[name] ?? match));
}

/** Wählt aus einer Browser-Sprache ("de-AT") die passende unterstützte Sprache. */
export function matchLanguage<T extends string>(
	candidate: string | undefined,
	supported: readonly T[],
	fallback: T
): T {
	const base = candidate?.toLowerCase().split('-')[0];
	return supported.find((s) => s === base) ?? fallback;
}
