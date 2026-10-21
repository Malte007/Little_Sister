import { api, type Camera, type LiveState, type SystemInfo } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent }) => {
	// Erst das Layout entscheiden lassen (Einrichtung/Login), sonst gäbe es unnötige 401-Anfragen
	await parent();
	const [info, cameras, live] = await Promise.all([
		api<SystemInfo>('/api/system/info', { fetch }),
		api<Camera[]>('/api/cameras', { fetch }),
		api<LiveState>('/api/live', { fetch })
	]);
	return { info, cameras, live };
};
