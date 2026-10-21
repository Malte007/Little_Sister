// Zahlen verständlich darstellen: Speicher in GB/TB, Dauer in Tagen.

const units = ['B', 'KB', 'MB', 'GB', 'TB'];

/** 1536 → "1,5 KB" (Einheiten zur Basis 1024, Dezimaltrennzeichen je Sprache). */
export function formatBytes(bytes: number | null | undefined, locale: string): string {
	if (bytes == null || bytes < 0) return '–';
	let value = bytes;
	let unit = 0;
	while (value >= 1024 && unit < units.length - 1) {
		value /= 1024;
		unit++;
	}
	const digits = unit === 0 || value >= 100 ? 0 : 1;
	return `${value.toLocaleString(locale, { maximumFractionDigits: digits, minimumFractionDigits: digits })} ${units[unit]}`;
}

/** Ganze Tage, bei weniger als einem Tag eine Nachkommastelle. */
export function formatDays(days: number | null | undefined, locale: string): string | null {
	if (days == null || !Number.isFinite(days)) return null;
	const digits = days < 1 ? 1 : 0;
	return days.toLocaleString(locale, {
		maximumFractionDigits: digits,
		minimumFractionDigits: digits
	});
}
