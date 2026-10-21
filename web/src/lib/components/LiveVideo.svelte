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
		rotation = 0,
		onlyWhenVisible = false
	}: {
		cameraId: string;
		/** Hauptstream statt Substream */
		hd?: boolean;
		/** Ton hörbar; angefordert wird er immer, manche Kameras senden sonst gar nichts */
		audio?: boolean;
		/** Drehung im Uhrzeigersinn; dreht nur die Anzeige, ohne Rechenaufwand */
		rotation?: number;
		/** Stream nur laden, solange die Kachel im sichtbaren Bereich ist */
		onlyWhenVisible?: boolean;
	} = $props();

	let host: HTMLDivElement;
	let player = $state<LivePlayer>();
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

	// Erst nur WebRTC (geringe Latenz), wie go2rtcs eigener Player. Liefert das beim ersten Versuch
	// kein Bild, folgt MSE über die normale Verbindung. Beides gleichzeitig geht nicht gut: scheitert
	// MSE, schließt der Player die gemeinsame Verbindung und reißt WebRTC mit.
	// Reißt die Verbindung später ab (Kamera weg, WLAN schlecht), baut der Player sie selbst neu auf.
	type Mode = 'webrtc' | 'mse';
	const startTimeout = 8000;
	const retryDelays = [3000, 5000, 10000, 20000, 30000];
	const stallTimeout = 10000;

	let mode: Mode = 'webrtc';
	let workingMode: Mode | null = null; // Verfahren, das zuletzt Bild geliefert hat
	let played = $state(false); // schon einmal Bild gehabt: Standbild nicht mehr nötig
	let attempt = 0;
	let webRtcError: string | null = null;
	let timer: ReturnType<typeof setTimeout> | undefined;
	let watchdog: ReturnType<typeof setInterval> | undefined;

	function start(next: Mode) {
		clearTimeout(timer);
		player?.remove();
		mode = next;
		playerState = 'connecting';
		error = null;

		const element = document.createElement('ls-live-video') as LivePlayer;
		element.mode = next;
		// Wie go2rtcs eigener Player: Bild und Ton anfordern
		element.media = 'video,audio';
		if (onlyWhenVisible) element.visibilityThreshold = 0.1;
		element.addEventListener('playerstate', (e) => {
			const { state, detail } = (e as CustomEvent<{ state: PlayerState; detail?: string }>).detail;
			if (state === 'playing') {
				clearTimeout(timer);
				workingMode = next;
				played = true;
				attempt = 0;
			}
			if (state === 'error') return failed(detail ?? null);
			playerState = state;
		});
		host.appendChild(element);
		element.src = `/api/live/${cameraId}/ws?hd=${hd}`;
		player = element;

		// Kein Bild in angemessener Zeit gilt ebenfalls als Fehlschlag
		timer = setTimeout(() => {
			if (playerState !== 'playing') failed(null);
		}, startTimeout);
	}

	function failed(detail: string | null) {
		clearTimeout(timer);
		if (mode === 'webrtc') webRtcError = detail ?? webRtcError;

		// Erster Versuch per WebRTC ohne Erfolg: sofort MSE probieren
		if (mode === 'webrtc' && workingMode === null) return start('mse');

		// Sonst Fehler zeigen und mit wachsender Pause neu verbinden, bevorzugt mit dem bewährten Verfahren
		playerState = 'error';
		error = detail ?? webRtcError;
		timer = setTimeout(
			() => start(workingMode ?? 'webrtc'),
			retryDelays[Math.min(attempt++, retryDelays.length - 1)]
		);
	}

	// Steht das Bild, obwohl eine Verbindung besteht (Kamera liefert nichts mehr), neu verbinden
	function startWatchdog() {
		let lastTime = -1;
		let lastChange = Date.now();
		watchdog = setInterval(() => {
			const video = player?.video;
			const connected = !!(player?.ws || player?.pc);
			if (!video || !connected || document.hidden || playerState !== 'playing') {
				lastChange = Date.now();
				return;
			}
			if (video.currentTime !== lastTime) {
				lastTime = video.currentTime;
				lastChange = Date.now();
			} else if (Date.now() - lastChange > stallTimeout) {
				lastChange = Date.now();
				failed(null);
			}
		}, 2000);
	}

	onMount(() => {
		definePlayer();
		start('webrtc');
		startWatchdog();
		return () => {
			clearTimeout(timer);
			clearInterval(watchdog);
			player?.remove();
		};
	});

	// Ton ein/aus ohne neue Verbindung
	$effect(() => {
		if (player?.video) player.video.muted = !audio;
	});

	const turned = $derived(rotation === 90 || rotation === 270);
</script>

<div class="live">
	<!-- Bei 90°/270° tauschen Breite und Höhe, damit das gedrehte Bild in die Kachel passt -->
	<div class="rotor" class:turned style:transform="translate(-50%, -50%) rotate({rotation}deg)">
		{#if !played && posterOk}
			<img
				src={poster}
				alt=""
				onerror={() => (posterOk = false)}
				transition:fade={{ duration: 200 }}
			/>
		{/if}
		<div class="player" bind:this={host}></div>
	</div>
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
		container-type: size;
	}
	.rotor {
		position: absolute;
		left: 50%;
		top: 50%;
		width: 100cqw;
		height: 100cqh;
	}
	.rotor.turned {
		width: 100cqh;
		height: 100cqw;
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
