<script lang="ts">
	// Spielt die Aufnahmen eines Tages per HLS ab (hls.js, auf Apple-Geräten ohne MSE nativ).
	// Eine Drehung steht als Markierung in den Dateien; der Browser dreht beim Abspielen selbst.
	import { onMount } from 'svelte';
	import { fade } from 'svelte/transition';
	import { t } from '#lib/i18n/index.svelte.js';
	import type Hls from 'hls.js';

	let {
		src,
		start = 0,
		rate = 1,
		audio = false,
		playing = $bindable(false),
		ontime,
		onended
	}: {
		/** Adresse der Playlist */
		src: string;
		/** Startposition im Video in Sekunden */
		start?: number;
		/** Abspielgeschwindigkeit, z. B. 4 für vierfach */
		rate?: number;
		audio?: boolean;
		playing?: boolean;
		/** Aktuelle Abspielposition in Sekunden */
		ontime?: (mediaTime: number) => void;
		onended?: () => void;
	} = $props();

	let video: HTMLVideoElement;
	let hls: Hls | undefined;
	let waiting = $state(true);
	let failed = $state(false);

	/** Springt an eine Stelle im Video (Sekunden). */
	export function seek(mediaTime: number) {
		if (!video) return;
		video.currentTime = Math.max(mediaTime, 0);
		ontime?.(video.currentTime);
	}

	onMount(() => {
		let destroyed = false;
		(async () => {
			const { default: HlsJs } = await import('hls.js');
			if (destroyed) return;

			if (HlsJs.isSupported()) {
				hls = new HlsJs({ startPosition: start, maxBufferLength: 30, backBufferLength: 60 });
				hls.on(HlsJs.Events.ERROR, (_, data) => {
					if (!data.fatal) return;
					// Netz- und Decoderfehler lassen sich meist ohne Neuladen beheben
					if (data.type === HlsJs.ErrorTypes.NETWORK_ERROR) hls?.startLoad();
					else if (data.type === HlsJs.ErrorTypes.MEDIA_ERROR) hls?.recoverMediaError();
					else failed = true;
				});
				hls.loadSource(src);
				hls.attachMedia(video);
			} else if (video.canPlayType('application/vnd.apple.mpegurl')) {
				video.addEventListener('loadedmetadata', () => (video.currentTime = start), { once: true });
				video.src = src;
			} else {
				failed = true;
			}
		})();

		return () => {
			destroyed = true;
			hls?.destroy();
		};
	});

	// Steuerung von außen: Abspielen/Pause, Geschwindigkeit und Ton
	$effect(() => {
		if (!video) return;
		if (playing && video.paused) video.play().catch(() => (playing = false));
		else if (!playing && !video.paused) video.pause();
	});
	$effect(() => {
		if (video) video.playbackRate = rate;
	});
	$effect(() => {
		if (video) video.muted = !audio;
	});
</script>

<div class="playback">
	<video
		bind:this={video}
		muted
		playsinline
		ontimeupdate={() => ontime?.(video.currentTime)}
		onwaiting={() => (waiting = true)}
		onseeking={() => (waiting = true)}
		onseeked={() => (waiting = false)}
		onloadeddata={() => (waiting = false)}
		onplaying={() => (waiting = false)}
		oncanplay={() => (waiting = false)}
		onplay={() => (playing = true)}
		onpause={() => (playing = false)}
		onratechange={() => {
			// Manche Browser setzen die Geschwindigkeit beim Laden zurück
			if (video.playbackRate !== rate) video.playbackRate = rate;
		}}
		onended={() => {
			playing = false;
			onended?.();
		}}
	></video>
	{#if failed}
		<div class="overlay" transition:fade={{ duration: 200 }}>
			<span class="error">{t('playback.failed')}</span>
		</div>
	{:else if waiting}
		<div class="overlay" transition:fade={{ duration: 200 }}>
			<span class="spinner" aria-label={t('live.connecting')}></span>
		</div>
	{/if}
</div>

<style>
	.playback {
		position: relative;
		aspect-ratio: 16 / 9;
		background: #000;
		overflow: hidden;
	}
	video {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
		object-fit: contain;
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
		border-radius: 999px;
		font-size: 0.85rem;
	}
	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}
</style>
