<script lang="ts">
	import { goto } from '$app/navigation';
	import { api, ApiError, type CameraInput } from '#lib/api.js';
	import CameraForm from '#lib/components/CameraForm.svelte';
	import { t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	let error = $state<string>();

	const initial = $derived<CameraInput>({ ...data.camera, password: null });

	async function save(input: CameraInput) {
		await api(`/api/cameras/${data.camera.id}`, { method: 'PUT', body: input });
		await goto('/cameras');
	}

	async function remove() {
		if (!confirm(t('cameras.deleteConfirm', { name: data.camera.name }))) return;
		const deleteRecordings = confirm(t('cameras.deleteRecordings'));
		try {
			await api(`/api/cameras/${data.camera.id}?deleteRecordings=${deleteRecordings}`, {
				method: 'DELETE'
			});
			await goto('/cameras');
		} catch (e) {
			error = e instanceof ApiError ? tError(e.code) : t('errors.network');
		}
	}
</script>

<div class="page">
	<h1>{t('cameras.editTitle')}</h1>
	{#if error}<div class="alert">{error}</div>{/if}
	<section class="card">
		{#key data.camera.id}
			<CameraForm
				{initial}
				cameraId={data.camera.id}
				hasPassword={data.camera.hasPassword}
				onsave={save}
			/>
		{/key}
	</section>
	<button class="danger" onclick={remove}>{t('cameras.delete')}</button>
</div>

<style>
	.page {
		max-width: 44rem;
	}
	.card {
		padding: 1.5rem;
		margin: 1rem 0 1.5rem;
	}
</style>
