import { redirect } from '@sveltejs/kit';
import { api, type Camera } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, params, parent }) => {
	const { user } = await parent();
	if (!user?.roles.includes('Admin')) redirect(307, '/cameras');
	return { camera: await api<Camera>(`/api/cameras/${params.id}`, { fetch }) };
};
