import { api, type Camera, type RecordingDay } from '#lib/api.js';
import { maxCameras } from '#lib/playback/sync.js';
import type { PageLoad } from './$types';

/**
 * Wiedergabe einer oder mehrerer Kameras: ?cameras=id1,id2&date=JJJJ-MM-TT&t=ISO-Zeit.
 * Ohne Auswahl die erste Kamera; unbekannte Kameras werden übergangen.
 */
export const load: PageLoad = async ({ fetch, parent, url, depends }) => {
	await parent();
	// Erlaubt der Seite, nur die Tagesdaten neu zu laden (z. B. heute jede Minute)
	depends('app:recording-day');
	const cameras = await api<Camera[]>('/api/cameras', { fetch });
	const wanted = (url.searchParams.get('cameras') ?? '').split(',').filter(Boolean);
	let selected = [...new Set(wanted)]
		.map((id) => cameras.find((c) => c.id === id))
		.filter((c): c is Camera => !!c)
		.slice(0, maxCameras);
	if (selected.length === 0) selected = cameras.slice(0, 1);

	const date = url.searchParams.get('date');
	const query = date ? `?date=${encodeURIComponent(date)}` : '';
	const days = await Promise.all(
		selected.map((c) => api<RecordingDay>(`/api/recordings/${c.id}/day${query}`, { fetch }))
	);
	return { cameras, selected, days, at: url.searchParams.get('t') };
};
