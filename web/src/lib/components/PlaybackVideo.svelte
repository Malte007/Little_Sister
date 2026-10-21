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
		playing = false,
		ontime,
		onended,
		onblocked
	}: {
		/** Adresse der Playlist */
		src: string;
		/** Startposition im Video in Sekunden */
		start?: number;
		/** Abspielgeschwindigkeit, z. B. 4 für vierfach */
		rate?: number;
		audio?: boolean;
		/** Soll laufen; in einer Lücke (siehe follow) pausiert das Video trotzdem */
		playing?: boolean;
		/** Aktuelle Abspielposition in Sekunden */
		ontime?: (mediaTime: number) => void;
		onended?: () => void;
		/** Der Browser hat das Abspielen verweigert (z. B. Autoplay-Sperre) */
		onblocked?: () => void;
	} = $props();

	let video: HTMLVideoElement;
	let hls: Hls | undefined;
	let waiting = $state(true);
	let failed = $state(false);
	// Keine Aufnahme zur aktuellen Uhrzeit (bei mehreren Kameras)
	let idle = $state(false);
	let lastSeek = 0;

	/** Springt an eine Stelle im Video (Sekunden). */
	export function seek(mediaTime: number) {
		if (!video) return;
		idle = false;
		lastSeek = Date.now();
		video.currentTime = Math.max(mediaTime, 0);
		resume();
		ontime?.(video.currentTime);
	}

	/**
	 * Folgt einer vorgegebenen Position: weicht das Video um mehr als tolerance Sekunden ab, springt es dorthin.
	 * null bedeutet: zu dieser Uhrzeit gibt es keine Aufnahme, das Video pausiert.
	 */
	export function follow(target: number | null, tolerance: number) {
		// Kommt die Kamera aus einer Lücke, genau an die Stelle springen
		if (idle && target !== null) tolerance = 0.3;
		idle = target === null;
		if (!video || target === null || video.readyState === 0) return;
		// Nach einem Sprung erst laden lassen, sonst springt ein langsames Video immer wieder
		if (
			Math.abs(video.currentTime - target) > tolerance &&
			(tolerance < 1 || Date.now() - lastSeek > 2000)
		) {
			lastSeek = Date.now();
			video.currentTime = target;
		}
		resume();
	}

	// Nur eine echte Sperre melden; ein abgebrochener Start (z. B. weil noch nichts geladen ist) zählt nicht
	function blocked(e: DOMException) {
		if (e.name === 'NotAllowedError') onblocked?.();
	}

	// Nach dem Ende oder einer Lücke weiterlaufen, sofern es laufen soll
	function resume() {
		if (playing && !idle && video.paused && !video.ended) video.play().catch(blocked);
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
		const run = playing && !idle;
		if (run && video.paused && !video.ended) video.play().catch(blocked);
		else if (!run && !video.paused) video.pause();
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
		ontimeupdate={() => {
			// Vor dem Laden und während eines Sprungs keine (veralteten) Positionen melden
			if (!video.seeking && video.readyState > 0) ontime?.(video.currentTime);
		}}
		onwaiting={() => (waiting = true)}
		onseeking={() => (waiting = true)}
		onseeked={() => (waiting = false)}
		onloadeddata={() => (waiting = false)}
		onplaying={() => (waiting = false)}
		oncanplay={() => {
			waiting = false;
			resume();
		}}
		onratechange={() => {
			// Manche Browser setzen die Geschwindigkeit beim Laden zurück
			if (video.playbackRate !== rate) video.playbackRate = rate;
		}}
		onended={() => onended?.()}
	></video>
	{#if idle}
		<div class="overlay dim" transition:fade={{ duration: 200 }}>
			<span class="note">{t('playback.noRecordingNow')}</span>
		</div>
	{:else if failed}
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
	.dim {
		background: rgb(0 0 0 / 0.55);
	}
	.note {
		color: #fff;
		font-size: 0.85rem;
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
