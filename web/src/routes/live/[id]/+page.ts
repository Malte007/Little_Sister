import { api, type Camera, type LiveState } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	await parent();
	const [camera, live] = await Promise.all([
		api<Camera>(`/api/cameras/${params.id}`, { fetch }),
		api<LiveState>('/api/live', { fetch })
	]);
	return { camera, live };
};
