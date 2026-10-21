import { api, type Camera, type StorageInfo } from '#lib/api.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ fetch, parent }) => {
	await parent();
	const [cameras, storage] = await Promise.all([
		api<Camera[]>('/api/cameras', { fetch }),
		api<StorageInfo>('/api/storage', { fetch })
	]);
	return { cameras, storage };
};
