<script lang="ts">
	import { onMount } from 'svelte';
	import { flip } from 'svelte/animate';
	import { fade, fly } from 'svelte/transition';
	import { invalidate } from '$app/navigation';
	import { api, ApiError } from '#lib/api.js';
	import { importCameras, type ImportedCamera } from '#lib/cameraTransfer.js';
	import CameraCard from '#lib/components/CameraCard.svelte';
	import CameraExport from '#lib/components/CameraExport.svelte';
	import Field from '#lib/components/Field.svelte';
	import StorageBar from '#lib/components/StorageBar.svelte';
	import { formatBytes } from '#lib/format.js';
	import { i18n, t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const isAdmin = $derived(data.user?.roles.includes('Admin') ?? false);

	// Status kommt live per SignalR; Speicherbelegung alle 30 s auffrischen
	onMount(() => {
		const timer = setInterval(() => invalidate((url) => url.pathname === '/api/cameras'), 30000);
		return () => clearInterval(timer);
	});

	// Speichereinstellungen (nur Admins): Aufnahmeordner, Freispeichergrenze und Vorschaubilder
	let minFree = $state('');
	let recordingsPath = $state('');
	let thumbnails = $state(true);
	let errors = $state<Record<string, string>>({});
	let saved = $state(false);
	$effect(() => {
		minFree = String(data.storage.minFreeGb);
		recordingsPath = data.storage.recordingsPath;
		thumbnails = data.storage.thumbnailsEnabled;
	});
	const pathChanged = $derived(recordingsPath.trim() !== data.storage.recordingsPath);

	async function saveStorage(event: SubmitEvent) {
		event.preventDefault();
		errors = {};
		saved = false;
		try {
			await api('/api/storage', {
				method: 'PUT',
				body: {
					minFreeGb: Number(minFree),
					recordingsPath: recordingsPath.trim(),
					thumbnailsEnabled: thumbnails
				}
			});
			await invalidate((url) => url.pathname === '/api/storage');
			saved = true;
		} catch (e) {
			if (!(e instanceof ApiError)) errors = { general: t('errors.network') };
			else if (e.field('recordingsPath') || e.field('minFreeGb'))
				errors = {
					recordingsPath: e.field('recordingsPath') ? tError(e.field('recordingsPath')) : '',
					minFreeGb: e.field('minFreeGb') ? tError(e.field('minFreeGb')) : ''
				};
			else errors = { general: tError(e.code) };
		}
	}

	// Import: Datei wählen, Kameras anlegen, Übersicht zeigen
	let importInput = $state<HTMLInputElement>();
	let imported = $state<ImportedCamera[]>();
	let importError = $state<string>();

	async function runImport() {
		const file = importInput?.files?.[0];
		if (!file) return;
		importError = undefined;
		imported = undefined;
		try {
			imported = await importCameras(file);
			await invalidate((url) => url.pathname === '/api/cameras');
		} catch (e) {
			importError = !(e instanceof ApiError)
				? t('errors.network')
				: (importFieldError(e) ?? tError(e.code));
		} finally {
			importInput!.value = '';
		}
	}

	/** Feldfehler wie "cameras[1].mainStreamUrl" als "Kamera 2: …" anzeigen. */
	function importFieldError(e: ApiError): string | undefined {
		const [key, codes] = Object.entries(e.fields)[0] ?? [];
		const index = key?.match(/^cameras\[(\d+)\]/)?.[1];
		return index === undefined
			? undefined
			: t('cameras.importCameraError', { number: Number(index) + 1, error: tError(codes[0]) });
	}

	const total = $derived(data.storage.totalBytes ?? 0);
	const used = $derived(total - (data.storage.freeBytes ?? 0));
</script>

<div class="head">
	<div>
		<h1>{t('cameras.title')}</h1>
		<p class="muted">{t('cameras.subtitle')}</p>
	</div>
	{#if isAdmin}<a class="button" href="/cameras/new">{t('cameras.add')}</a>{/if}
</div>

{#if data.cameras.length === 0}
	<p class="card empty muted">{t('cameras.empty')}</p>
{:else}
	<div class="grid">
		{#each data.cameras as camera, i (camera.id)}
			<div animate:flip={{ duration: 250 }} in:fly={{ y: 12, delay: i * 40, duration: 350 }}>
				<CameraCard {camera} editable={isAdmin} />
			</div>
		{/each}
	</div>
{/if}

{#if isAdmin}
	<section class="card transfer">
		<h2>{t('cameras.transferTitle')}</h2>
		<p class="muted small">{t('cameras.transferHint')}</p>
		<div class="transfer-grid">
			{#if data.cameras.length > 0}
				<CameraExport name="kameras" />
			{/if}
			<div>
				<input
					bind:this={importInput}
					type="file"
					accept=".json,application/json"
					hidden
					onchange={runImport}
				/>
				<button class="secondary" onclick={() => importInput?.click()}>{t('cameras.import')}</button
				>
			</div>
		</div>
		{#if importError}<div class="alert">{importError}</div>{/if}
		{#if imported}
			<div class="imported" transition:fade>
				<p>{t('cameras.importDone', { count: imported.length })}</p>
				<ul>
					{#each imported as camera (camera.id)}
						<li>
							<a href="/cameras/{camera.id}">{camera.name}</a>
							{#if camera.needsPassword}<span class="needs">{t('cameras.importNeedsPassword')}</span
								>{/if}
						</li>
					{/each}
				</ul>
			</div>
		{/if}
	</section>
{/if}

<section class="card storage">
	<h2>{t('storage.title')}</h2>
	{#if data.storage.totalBytes}
		<StorageBar
			value={used}
			max={total}
			label={t('storage.free', {
				free: formatBytes(data.storage.freeBytes, i18n.language),
				total: formatBytes(total, i18n.language)
			})}
		/>
	{/if}

	{#if isAdmin}
		<form class="settings" onsubmit={saveStorage}>
			{#if errors.general}<div class="alert">{errors.general}</div>{/if}
			<Field
				label={t('storage.path')}
				bind:value={recordingsPath}
				spellcheck="false"
				hint={pathChanged ? t('storage.pathChangeHint') : t('storage.pathHint')}
				error={errors.recordingsPath}
			/>
			<Field
				label={t('storage.minFree')}
				type="number"
				min="0"
				step="1"
				bind:value={minFree}
				hint={t('storage.minFreeHint')}
				error={errors.minFreeGb}
			/>
			<label class="toggle">
				<input type="checkbox" bind:checked={thumbnails} />
				<span>
					<strong>{t('storage.thumbnails')}</strong>
					<span class="muted small">{t('storage.thumbnailsHint')}</span>
				</span>
			</label>
			<div class="save">
				<button type="submit" class="secondary">{t('common.save')}</button>
				{#if saved}<span class="saved" transition:fade>{t('storage.saved')}</span>{/if}
			</div>
		</form>
	{:else}
		<p class="muted small">{t('storage.path')}: <code>{data.storage.recordingsPath}</code></p>
	{/if}
</section>

<style>
	.head {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 1rem;
		flex-wrap: wrap;
		margin-bottom: 1.5rem;
	}
	.grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
		gap: 1rem;
	}
	.empty {
		text-align: center;
		padding: 2.5rem;
	}
	.transfer {
		margin-top: 2rem;
		padding: 1.5rem;
	}
	.transfer h2 {
		font-size: 1.1rem;
		margin-bottom: 0.25rem;
	}
	.transfer-grid {
		display: flex;
		gap: 2rem;
		flex-wrap: wrap;
		align-items: flex-end;
		margin-top: 1rem;
	}
	.imported {
		margin-top: 1rem;
	}
	.imported ul {
		margin: 0.5rem 0 0;
		padding-left: 1.25rem;
	}
	.needs {
		margin-left: 0.5rem;
		font-size: 0.8rem;
		color: var(--danger);
	}
	.storage {
		margin-top: 2rem;
		padding: 1.5rem;
	}
	.storage h2 {
		font-size: 1.1rem;
		margin-bottom: 1rem;
	}
	.small {
		font-size: 0.8rem;
	}
	code {
		font-size: 0.8rem;
	}
	.settings {
		margin-top: 1.5rem;
		max-width: 36rem;
	}
	.toggle {
		display: flex;
		gap: 0.75rem;
		align-items: flex-start;
		font-weight: 400;
		margin-bottom: 1.25rem;
		padding: 0.9rem 1rem;
		border: 1px solid var(--border);
		border-radius: 10px;
		cursor: pointer;
	}
	.toggle input {
		margin-top: 0.25rem;
	}
	.toggle span {
		display: flex;
		flex-direction: column;
	}
	.save {
		display: flex;
		align-items: center;
		gap: 1rem;
	}
	.saved {
		color: var(--success);
		font-size: 0.875rem;
	}
</style>
