import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ parent }) => {
	const { user } = await parent();
	if (!user?.roles.includes('Admin')) redirect(307, '/cameras');
	return {};
};
