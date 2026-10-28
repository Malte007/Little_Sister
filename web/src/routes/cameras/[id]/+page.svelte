<script lang="ts">
	import { goto } from '$app/navigation';
	import { api, ApiError, type CameraInput } from '#lib/api.js';
	import CameraExport from '#lib/components/CameraExport.svelte';
	import CameraForm from '#lib/components/CameraForm.svelte';
	import Field from '#lib/components/Field.svelte';
	import { t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	let error = $state<string>();

	// "Alle Aufnahmen löschen": erst nach Eingabe des Kameranamens freigegeben
	let wipeOpen = $state(false);
	let wipeName = $state('');
	let wipeBusy = $state(false);
	let wipeResult = $state<string>();

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

	async function wipeRecordings() {
		wipeBusy = true;
		error = undefined;
		try {
			const result = await api<{ deleted: number; skipped: number }>(
				`/api/cameras/${data.camera.id}/recordings`,
				{ method: 'DELETE' }
			);
			wipeResult = result.skipped
				? t('cameras.wipeDoneSkipped', { count: result.deleted, skipped: result.skipped })
				: t('cameras.wipeDone', { count: result.deleted });
			wipeOpen = false;
			wipeName = '';
		} catch (e) {
			error = e instanceof ApiError ? tError(e.code) : t('errors.network');
		} finally {
			wipeBusy = false;
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
	<section class="card">
		<h2>{t('cameras.exportTitle')}</h2>
		<p class="muted">{t('cameras.exportHint')}</p>
		<CameraExport ids={[data.camera.id]} name={data.camera.name} />
	</section>
	<section class="card danger-zone">
		<h2>{t('cameras.wipeTitle')}</h2>
		<p class="muted">{t('cameras.wipeHint')}</p>
		{#if wipeResult}<p class="done">{wipeResult}</p>{/if}
		{#if wipeOpen}
			<Field
				label={t('cameras.wipeConfirm', { name: data.camera.name })}
				bind:value={wipeName}
				autocomplete="off"
			/>
			<div class="actions">
				<button
					class="danger"
					disabled={wipeBusy || wipeName.trim() !== data.camera.name.trim()}
					onclick={wipeRecordings}>{t('cameras.wipe')}</button
				>
				<button class="secondary" onclick={() => ((wipeOpen = false), (wipeName = ''))}
					>{t('common.cancel')}</button
				>
			</div>
		{:else}
			<button class="danger" onclick={() => ((wipeOpen = true), (wipeResult = undefined))}
				>{t('cameras.wipe')}</button
			>
		{/if}
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
	.card h2 {
		margin-top: 0;
		font-size: 1.1rem;
	}
	.actions {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
	}
	.done {
		font-weight: 600;
	}
</style>
