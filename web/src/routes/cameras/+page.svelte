<script lang="ts">
	import { onMount } from 'svelte';
	import { flip } from 'svelte/animate';
	import { fly } from 'svelte/transition';
	import { invalidate } from '$app/navigation';
	import { api, ApiError } from '#lib/api.js';
	import CameraCard from '#lib/components/CameraCard.svelte';
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

	let minFree = $state('');
	let minFreeError = $state<string>();
	$effect(() => {
		minFree = String(data.storage.minFreeGb);
	});

	async function saveMinFree() {
		minFreeError = undefined;
		try {
			await api('/api/storage', { method: 'PUT', body: { minFreeGb: Number(minFree) } });
			await invalidate((url) => url.pathname === '/api/storage');
		} catch (e) {
			minFreeError =
				e instanceof ApiError ? tError(e.field('minFreeGb') ?? e.code) : t('errors.network');
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
	<p class="muted small">{t('storage.path')}: <code>{data.storage.recordingsPath}</code></p>

	{#if isAdmin}
		<form
			class="minfree"
			onsubmit={(e) => {
				e.preventDefault();
				saveMinFree();
			}}
		>
			<div>
				<label for="minfree">{t('storage.minFree')}</label>
				<input id="minfree" type="number" min="0" step="1" bind:value={minFree} />
				{#if minFreeError}<p class="error">{minFreeError}</p>{/if}
				<p class="muted small">{t('storage.minFreeHint')}</p>
			</div>
			<button type="submit" class="secondary">{t('common.save')}</button>
		</form>
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
	.minfree {
		display: flex;
		align-items: flex-start;
		gap: 1rem;
		margin-top: 1rem;
		max-width: 32rem;
	}
	.minfree input {
		max-width: 10rem;
	}
	.minfree button {
		margin-top: 1.6rem;
	}
</style>
