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
</script>

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
	</div>
</div>

{#if liveUsable}
	<div class="card stage">
		<!-- Neu aufbauen, wenn Auflösung oder Ton wechseln -->
		{#key `${hd}-${audio}`}
			<LiveVideo cameraId={camera.id} hd={hasSub && hd} {audio} />
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
	.stage {
		padding: 0;
		overflow: hidden;
	}
</style>
