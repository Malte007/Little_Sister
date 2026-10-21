<script lang="ts">
	import { onMount } from 'svelte';
	import { flip } from 'svelte/animate';
	import { fade, fly } from 'svelte/transition';
	import { invalidate } from '$app/navigation';
	import { api, ApiError } from '#lib/api.js';
	import CameraCard from '#lib/components/CameraCard.svelte';
	import Field from '#lib/components/Field.svelte';
	import StorageBar from '#lib/components/StorageBar.svelte';
	import { formatBytes } from '#lib/format.js';
	import { i18n, t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const isAdmin = $derived(data.user?.roles.includes('Admin') ?? false);

	// Status alle 5 s auffrischen; live per SignalR folgt mit der Live-Ansicht
	onMount(() => {
		const timer = setInterval(() => invalidate((url) => url.pathname === '/api/cameras'), 5000);
		return () => clearInterval(timer);
	});

	// Speichereinstellungen (nur Admins): Aufnahmeordner und Freispeichergrenze
	let minFree = $state('');
	let recordingsPath = $state('');
	let errors = $state<Record<string, string>>({});
	let saved = $state(false);
	$effect(() => {
		minFree = String(data.storage.minFreeGb);
		recordingsPath = data.storage.recordingsPath;
	});
	const pathChanged = $derived(recordingsPath.trim() !== data.storage.recordingsPath);

	async function saveStorage(event: SubmitEvent) {
		event.preventDefault();
		errors = {};
		saved = false;
		try {
			await api('/api/storage', {
				method: 'PUT',
				body: { minFreeGb: Number(minFree), recordingsPath: recordingsPath.trim() }
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
