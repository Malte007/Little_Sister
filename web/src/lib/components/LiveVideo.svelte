<script lang="ts">
	// Live-Bild einer Kamera; bis das Video läuft, wird ein Standbild gezeigt
	import { onMount } from 'svelte';
	import { fade } from 'svelte/transition';
	import { t } from '#lib/i18n/index.svelte.js';
	import { definePlayer, type LivePlayer, type PlayerState } from '#lib/live/player.js';

	let {
		cameraId,
		hd = false,
		audio = false,
		onlyWhenVisible = false
	}: {
		cameraId: string;
		/** Hauptstream statt Substream */
		hd?: boolean;
		audio?: boolean;
		/** Stream nur laden, solange die Kachel im sichtbaren Bereich ist */
		onlyWhenVisible?: boolean;
	} = $props();

	let host: HTMLDivElement;
	let playerState = $state<PlayerState>('connecting');
	let error = $state<string | null>(null);
	// Standbild als Platzhalter; Zeitstempel verhindert ein veraltetes Bild aus dem Browser-Cache
	const poster = $derived(`/api/live/${cameraId}/snapshot?hd=${hd}&t=${Date.now()}`);
	let posterOk = $state(true);

	// go2rtc meldet z. B. "codecs not matched: video:H265 => video:VP8, …", wenn der Browser das Format nicht kann
	const errorText = $derived.by(() => {
		const codec = error?.includes('codecs not matched') ? error.match(/video:(\w+)/)?.[1] : null;
		return codec ? t('live.codecUnsupported', { codec }) : t('live.cameraError');
	});

	onMount(() => {
		definePlayer();
		const player = document.createElement('ls-live-video') as LivePlayer;
		player.mode = 'webrtc,mse';
		player.media = audio ? 'video,audio' : 'video';
		if (onlyWhenVisible) player.visibilityThreshold = 0.1;
		player.addEventListener('playerstate', (e) => {
			const { state: next, detail } = (e as CustomEvent<{ state: PlayerState; detail?: string }>)
				.detail;
			playerState = next;
			error = next === 'error' ? (detail ?? null) : null;
		});
		host.appendChild(player);
		player.src = `/api/live/${cameraId}/ws?hd=${hd}`;
		return () => player.remove();
	});
</script>

<div class="live">
	{#if playerState !== 'playing' && posterOk}
		<img
			src={poster}
			alt=""
			onerror={() => (posterOk = false)}
			transition:fade={{ duration: 200 }}
		/>
	{/if}
	<div class="player" bind:this={host}></div>
	{#if playerState !== 'playing'}
		<div class="overlay" transition:fade={{ duration: 200 }}>
			{#if playerState === 'error'}
				<span class="error" title={error ?? ''}>{errorText}</span>
			{:else}
				<span class="spinner" aria-label={t('live.connecting')}></span>
			{/if}
		</div>
	{/if}
</div>

<style>
	.live {
		position: relative;
		aspect-ratio: 16 / 9;
		background: #000;
		overflow: hidden;
	}
	.player,
	img {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
	}
	img {
		object-fit: contain;
		filter: blur(2px) brightness(0.7);
	}
	.overlay {
		position: absolute;
		inset: 0;
		display: grid;
		place-items: center;
		pointer-events: none;
	}
	.spinner {
		width: 2rem;
		height: 2rem;
		border: 3px solid rgb(255 255 255 / 0.25);
		border-top-color: #fff;
		border-radius: 50%;
		animation: spin 0.9s linear infinite;
	}
	.error {
		color: #fff;
		background: color-mix(in srgb, var(--danger) 70%, transparent);
		padding: 0.3rem 0.8rem;
		margin: 0 1rem;
		text-align: center;
		border-radius: 999px;
		font-size: 0.85rem;
	}
	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}
</style>
