import { redirect } from '@sveltejs/kit';
import { api, ApiError, type CurrentUser, type SetupStatus } from '#lib/api.js';
import type { LayoutLoad } from './$types';

// Reine Single-Page-App: kein Server-Rendering, alles läuft im Browser
export const ssr = false;
export const prerender = false;

/** Leitet je nach Zustand zur Einrichtung, zum Login oder in die Oberfläche. */
export const load: LayoutLoad = async ({ fetch, url }) => {
	const setup = await api<SetupStatus>('/api/setup', { fetch });
	const path = url.pathname;

	if (setup.required) {
		if (path !== '/setup') redirect(307, '/setup');
		return { setup, user: null };
	}
	if (path === '/setup') redirect(307, '/');

	let user: CurrentUser | null = null;
	try {
		user = await api<CurrentUser>('/api/auth/me', { fetch });
	} catch (e) {
		if (!(e instanceof ApiError && e.status === 401)) throw e;
	}

	if (!user && path !== '/login') redirect(307, '/login');
	if (user && path === '/login') redirect(307, '/');

	return { setup, user };
};
