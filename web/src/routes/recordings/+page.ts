import { redirect } from '@sveltejs/kit';
import { api, type Camera } from '#lib/api.js';
import type { PageLoad } from './$types';

/** Ohne gewählte Kamera direkt zur ersten weiterleiten. */
export const load: PageLoad = async ({ fetch, parent }) => {
	await parent();
	const cameras = await api<Camera[]>('/api/cameras', { fetch });
	if (cameras.length > 0) redirect(307, `/recordings/${cameras[0].id}`);
	return {};
};
