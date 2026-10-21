<script lang="ts">
	// Einzelansicht einer Kamera: großes Live-Bild, wahlweise in voller Auflösung und mit Ton
	import LiveVideo from '#lib/components/LiveVideo.svelte';
	import StatusPill from '#lib/components/StatusPill.svelte';
	import { t } from '#lib/i18n/index.svelte.js';
	import { statusOf } from '#lib/live/status.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const camera = $derived(data.camera);
	const status = $derived(statusOf(camera));
	const hasSub = $derived(camera.subStreamUrl !== null);

	// Mit Substream startet die Einzelansicht in voller Auflösung, umschaltbar bei schwachem Netz
	let hd = $state(true);
	let audio = $state(false);
	const liveUsable = $derived(data.live.available || data.live.error === 'starting');

	// Vollbild per Knopf oder Doppelklick; auf Geräten ohne Vollbild-API wird der Knopf ausgeblendet
	let stage = $state<HTMLDivElement>();
	let fullscreen = $state(false);
	const canFullscreen = typeof document !== 'undefined' && document.fullscreenEnabled;

	function toggleFullscreen() {
		if (document.fullscreenElement) document.exitFullscreen();
		else stage?.requestFullscreen();
	}
</script>

<svelte:document onfullscreenchange={() => (fullscreen = document.fullscreenElement === stage)} />

<div class="head">
	<div>
		<a class="back muted" href="/">← {t('nav.overview')}</a>
		<h1>{camera.name}</h1>
	</div>
	<div class="controls">
		<StatusPill state={camera.recordingEnabled ? status.state : 'Idle'} />
		{#if hasSub}
			<div class="segmented" role="group" aria-label={t('live.quality')}>
				<button class:active={!hd} onclick={() => (hd = false)}>{t('live.sd')}</button>
				<button class:active={hd} onclick={() => (hd = true)}>{t('live.hd')}</button>
			</div>
		{/if}
		<button class="secondary" onclick={() => (audio = !audio)} aria-pressed={audio}>
			{audio ? t('live.audioOn') : t('live.audioOff')}
		</button>
		{#if canFullscreen}
			<button class="secondary" onclick={toggleFullscreen}>{t('live.fullscreen')}</button>
		{/if}
	</div>
</div>

{#if liveUsable}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="card stage"
		class:fullscreen
		bind:this={stage}
		ondblclick={canFullscreen ? toggleFullscreen : undefined}
		title={canFullscreen ? t('live.fullscreenHint') : undefined}
	>
		<!-- Neu aufbauen, wenn die Auflösung wechselt -->
		{#key hd}
			<LiveVideo cameraId={camera.id} hd={hasSub && hd} {audio} rotation={camera.rotation} />
		{/key}
	</div>
{:else}
	<p class="alert">{t(`live.${data.live.error ?? 'go2rtcFailed'}`)}</p>
{/if}

<style>
	.head {
		display: flex;
		justify-content: space-between;
		align-items: flex-end;
		gap: 1rem;
		flex-wrap: wrap;
		margin-bottom: 1.25rem;
	}
	.back {
		font-size: 0.875rem;
		text-decoration: none;
	}
	h1 {
		margin-top: 0.25rem;
	}
	.controls {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
	}
	.segmented {
		display: inline-flex;
		border: 1px solid var(--border);
		border-radius: 10px;
		overflow: hidden;
	}
	.segmented button {
		border-radius: 0;
		padding: 0.5rem 0.9rem;
		background: transparent;
		color: var(--text-muted);
	}
	.segmented button.active {
		background: var(--accent);
		color: #fff;
	}
	/* So groß wie möglich: volle Fensterbreite, aber nie höher als der Platz unter der Kopfzeile */
	.stage {
		position: relative;
		left: 50%;
		translate: -50%;
		width: min(calc(100vw - 3rem), calc((100dvh - 13rem) * 16 / 9));
		min-width: min(100%, 20rem);
		padding: 0;
		overflow: hidden;
	}
	.stage.fullscreen {
		left: 0;
		translate: none;
		width: 100vw;
		height: 100vh;
		display: grid;
		place-items: center;
		border: 0;
		border-radius: 0;
		background: #000;
	}
	/* Im Vollbild füllt das Bild den Bildschirm, ohne verzerrt zu werden */
	.stage.fullscreen :global(.live) {
		width: min(100vw, calc(100vh * 16 / 9));
	}
</style>
