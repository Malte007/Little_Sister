import { api, type Camera, type SystemInfo } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent }) => {
	// Erst das Layout entscheiden lassen (Einrichtung/Login), sonst gäbe es unnötige 401-Anfragen
	await parent();
	const [info, cameras] = await Promise.all([
		api<SystemInfo>('/api/system/info', { fetch }),
		api<Camera[]>('/api/cameras', { fetch })
	]);
	return { info, cameras };
};
