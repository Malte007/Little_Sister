<script lang="ts">
	import { onMount } from 'svelte';
	import { fly } from 'svelte/transition';
	import { invalidate } from '$app/navigation';
	import CameraCard from '#lib/components/CameraCard.svelte';
	import LiveTile from '#lib/components/LiveTile.svelte';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	// Sprache der Installation gilt, solange auf diesem Gerät keine eigene gewählt wurde
	$effect(() => i18n.useDefault(data.info.language));

	const isAdmin = $derived(data.user?.roles.includes('Admin') ?? false);
	const role = $derived(isAdmin ? 'Admin' : 'Viewer');

	// Status kommt live per SignalR; Kameraliste und Live-Zustand gelegentlich neu laden
	onMount(() => {
		const timer = setInterval(
			() => invalidate((url) => url.pathname === '/api/cameras' || url.pathname === '/api/live'),
			30000
		);
		return () => clearInterval(timer);
	});

	// Startet go2rtc gerade noch, trotzdem schon Live-Kacheln zeigen (sie verbinden sich selbst neu)
	const liveUsable = $derived(data.live.available || data.live.error === 'starting');
</script>

<section in:fly={{ y: 12, duration: 400 }}>
	<h1>{t('home.greeting', { name: data.user?.userName ?? '' })}</h1>
	<p class="muted">{t('home.signedInAs', { role: t(`roles.${role}`) })}</p>

	{#if data.cameras.length > 0}
		{#if !liveUsable}
			<p class="alert notice">{t(`live.${data.live.error ?? 'go2rtcFailed'}`)}</p>
		{/if}
		<div class="grid" class:live={liveUsable}>
			{#each data.cameras as camera (camera.id)}
				{#if liveUsable}
					<LiveTile {camera} />
				{:else}
					<CameraCard {camera} />
				{/if}
			{/each}
		</div>
	{:else}
		<div class="card empty">
			<div class="lens" aria-hidden="true"></div>
			<h2>{t('home.camerasTitle')}</h2>
			<p class="muted">{isAdmin ? t('home.camerasText') : t('home.askAdmin')}</p>
			{#if isAdmin}<a class="button add" href="/cameras/new">{t('home.addCamera')}</a>{/if}
		</div>
	{/if}

	<p class="muted version">{t('home.version', { version: data.info.version })}</p>
</section>

<style>
	h1 {
		font-size: 1.8rem;
	}
	.grid {
		margin-top: 2rem;
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
		gap: 1rem;
	}
	.grid.live {
		grid-template-columns: repeat(auto-fill, minmax(min(100%, 24rem), 1fr));
	}
	.notice {
		margin-top: 1.5rem;
	}
	.add {
		margin-top: 1.5rem;
	}
	.empty {
		margin-top: 2rem;
		text-align: center;
		padding: 3.5rem 2rem;
	}
	.empty p {
		max-width: 32rem;
		margin: 0 auto;
	}
	.lens {
		width: 4rem;
		height: 4rem;
		margin: 0 auto 1.25rem;
		border-radius: 50%;
		border: 3px solid var(--accent);
		position: relative;
		animation: pulse 2.8s var(--ease) infinite;
	}
	.lens::after {
		content: '';
		position: absolute;
		inset: 1.2rem;
		border-radius: 50%;
		background: var(--accent);
	}
	@keyframes pulse {
		0%,
		100% {
			box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 40%, transparent);
		}
		50% {
			box-shadow: 0 0 0 14px transparent;
		}
	}
	.version {
		margin-top: 2rem;
		font-size: 0.8rem;
	}
</style>
