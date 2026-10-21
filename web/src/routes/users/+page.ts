import { redirect } from '@sveltejs/kit';
import { api, type UserInfo } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent }) => {
	// Betrachter sehen die Benutzerverwaltung nicht
	const { user } = await parent();
	if (!user?.roles.includes('Admin')) redirect(307, '/');
	return { users: await api<UserInfo[]>('/api/users', { fetch }) };
};
