import { error } from '@sveltejs/kit';
import { api, type Camera, type RecordingDay } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent, params, url, depends }) => {
	await parent();
	// Erlaubt der Seite, nur die Tagesdaten neu zu laden (z. B. heute jede Minute)
	depends('app:recording-day');
	const date = url.searchParams.get('date');
	const query = date ? `?date=${encodeURIComponent(date)}` : '';
	const [cameras, day] = await Promise.all([
		api<Camera[]>('/api/cameras', { fetch }),
		api<RecordingDay>(`/api/recordings/${params.id}/day${query}`, { fetch })
	]);
	const camera = cameras.find((c) => c.id === params.id);
	if (!camera) error(404);
	return { camera, cameras, day, at: url.searchParams.get('t') };
};
