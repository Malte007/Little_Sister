<script lang="ts">
	// Wiedergabe einer Kamera: Tag wählen, auf der Zeitleiste springen, Zeitraum als MP4 exportieren
	import { onMount, untrack } from 'svelte';
	import { goto, invalidate } from '$app/navigation';
	import PlaybackVideo from '#lib/components/PlaybackVideo.svelte';
	import Timeline from '#lib/components/Timeline.svelte';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import { buildMap, mediaLength, mergeRanges, toMedia, toWall } from '#lib/playback/timeline.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const camera = $derived(data.camera);
	const day = $derived(data.day);
	const isToday = $derived(day.date === day.today);
	const from = $derived(Date.parse(day.fromUtc));
	const to = $derived(Date.parse(day.toUtc));
	const map = $derived(buildMap(day.segments));
	const ranges = $derived(mergeRanges(map));
	const events = $derived(day.events.map((e) => ({ time: Date.parse(e.timeUtc), kind: e.kind })));
	const playlist = $derived(`/api/recordings/${camera.id}/day/${day.date}/index.m3u8`);

	let player = $state<ReturnType<typeof PlaybackVideo>>();
	let playing = $state(true);
	let rate = $state(1);
	let audio = $state(false);
	let position = $state<number | null>(null);
	// Neuaufbau des Players, z. B. wenn heute neue Aufnahmen dazukommen
	let reload = $state(0);
	let start = $state(0);

	// Startposition bei neuem Tag oder neuer Kamera: gewünschte Uhrzeit, heute die letzten fünf Minuten,
	// sonst Tagesbeginn. Das minütliche Auffrischen von heute ändert sie nicht.
	$effect(() => {
		void playlist;
		untrack(() => {
			const segments = map;
			const at = data.at ? Date.parse(data.at) : NaN;
			const length = mediaLength(segments);
			start = !Number.isNaN(at)
				? (toMedia(segments, at) ?? 0)
				: isToday
					? Math.max(length - 300, 0)
					: 0;
			position = toWall(segments, start);
		});
	});

	function seekWall(wall: number) {
		const media = toMedia(map, wall);
		if (media !== null) player?.seek(media);
	}

	// Heute wächst die Aufnahme weiter: Zeitleiste jede Minute auffrischen
	onMount(() => {
		const timer = setInterval(() => {
			if (isToday && !document.hidden) invalidate('app:recording-day');
		}, 60_000);
		return () => clearInterval(timer);
	});

	// Am Ende von heute neue Segmente holen und an derselben Stelle weiterspielen
	async function ended() {
		if (!isToday || position === null) return;
		const last = position;
		await invalidate('app:recording-day');
		const next = toMedia(map, last + 1);
		if (next === null) return;
		start = next;
		playing = true;
		reload++;
	}

	function changeDate(date: string | null) {
		if (!date) return;
		goto(`?date=${date}`, { reset: false });
	}

	function changeCamera(id: string) {
		goto(`/recordings/${id}?date=${day.date}`, { reset: false });
	}

	// Export: Anfang und Ende an der aktuellen Position setzen
	let selection = $state<{ start: number | null; end: number | null }>({ start: null, end: null });
	const maxExport = 24 * 3_600_000;
	const exportError = $derived.by(() => {
		const { start: s, end: e } = selection;
		if (s === null || e === null) return null;
		if (e <= s) return t('playback.endBeforeStart');
		if (e - s > maxExport) return t('playback.exportTooLong');
		return null;
	});
	const exportUrl = $derived(
		selection.start !== null && selection.end !== null && !exportError
			? `/api/recordings/${camera.id}/export?fromUtc=${new Date(selection.start).toISOString()}&toUtc=${new Date(selection.end).toISOString()}`
			: null
	);

	// Neuer Tag oder neue Kamera: Auswahl verwerfen
	$effect(() => {
		void day.date;
		void camera.id;
		selection = { start: null, end: null };
	});

	const clock = $derived(
		new Intl.DateTimeFormat(i18n.language, {
			timeZone: day.timeZoneId,
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit'
		})
	);
	const longDate = $derived(
		new Intl.DateTimeFormat(i18n.language, {
			timeZone: day.timeZoneId,
			weekday: 'long',
			day: 'numeric',
			month: 'long',
			year: 'numeric'
		})
	);
	const firstDate = $derived(
		camera.storage.oldestUtc
			? new Intl.DateTimeFormat('en-CA', { timeZone: day.timeZoneId }).format(
					Date.parse(camera.storage.oldestUtc)
				)
			: day.date
	);

	// Tastatur: Leertaste spielt/pausiert, sofern kein Eingabefeld aktiv ist
	function keydown(e: KeyboardEvent) {
		const target = e.target as HTMLElement;
		if (e.key !== ' ' || ['INPUT', 'SELECT', 'BUTTON', 'TEXTAREA'].includes(target.tagName)) return;
		e.preventDefault();
		playing = !playing;
	}
</script>

<svelte:window onkeydown={keydown} />

<div class="head">
	<div>
		<a class="back muted" href="/live/{camera.id}">← {t('playback.toLive')}</a>
		<h1>{t('playback.title')}</h1>
	</div>
	<div class="controls">
		{#if data.cameras.length > 1}
			<select
				aria-label={t('playback.camera')}
				value={camera.id}
				onchange={(e) => changeCamera(e.currentTarget.value)}
			>
				{#each data.cameras as c (c.id)}
					<option value={c.id}>{c.name}</option>
				{/each}
			</select>
		{:else}
			<strong>{camera.name}</strong>
		{/if}
		<div class="date">
			<button
				class="secondary"
				disabled={!day.previousDate}
				onclick={() => changeDate(day.previousDate)}
				aria-label={t('playback.previousDay')}>‹</button
			>
			<input
				type="date"
				value={day.date}
				min={firstDate}
				max={day.today}
				aria-label={t('playback.date')}
				onchange={(e) => changeDate(e.currentTarget.value)}
			/>
			<button
				class="secondary"
				disabled={!day.nextDate}
				onclick={() => changeDate(day.nextDate)}
				aria-label={t('playback.nextDay')}>›</button
			>
		</div>
	</div>
</div>

<p class="muted day">{longDate.format(from)}</p>

<div class="card stage">
	{#if day.segments.length > 0}
		{#key `${playlist}#${reload}`}
			<PlaybackVideo
				bind:this={player}
				bind:playing
				src={playlist}
				{start}
				{rate}
				{audio}
				ontime={(m) => (position = toWall(map, m))}
				onended={ended}
			/>
		{/key}
	{:else}
		<div class="empty">
			<p>{t('playback.noRecordings')}</p>
		</div>
	{/if}
</div>

{#if day.segments.length > 0}
	<div class="bar">
		<button onclick={() => (playing = !playing)} class="play">
			{playing ? t('playback.pause') : t('playback.play')}
		</button>
		<span class="clock">{position !== null ? clock.format(position) : '–'}</span>
		<div class="segmented" role="group" aria-label={t('playback.speed')}>
			{#each [1, 2, 4, 8] as r (r)}
				<button class:active={rate === r} onclick={() => (rate = r)}>{r}×</button>
			{/each}
		</div>
		<button class="secondary" onclick={() => (audio = !audio)} aria-pressed={audio}>
			{audio ? t('live.audioOn') : t('live.audioOff')}
		</button>
	</div>
{/if}

<div class="card timeline">
	<Timeline
		{from}
		{to}
		{ranges}
		{events}
		{position}
		{selection}
		timeZone={day.timeZoneId}
		onseek={seekWall}
	/>
</div>

{#if day.segments.length > 0}
	<section class="card export">
		<h2>{t('playback.export')}</h2>
		<p class="muted">{t('playback.exportHint')}</p>
		<div class="export-row">
			<button
				class="secondary"
				disabled={position === null}
				onclick={() => (selection = { ...selection, start: position })}
			>
				{t('playback.setStart')}
				{selection.start !== null ? clock.format(selection.start) : ''}
			</button>
			<button
				class="secondary"
				disabled={position === null}
				onclick={() => (selection = { ...selection, end: position })}
			>
				{t('playback.setEnd')}
				{selection.end !== null ? clock.format(selection.end) : ''}
			</button>
			{#if exportUrl}
				<a class="button" href={exportUrl} download>{t('playback.download')}</a>
			{/if}
			{#if selection.start !== null || selection.end !== null}
				<button class="secondary" onclick={() => (selection = { start: null, end: null })}>
					{t('playback.clear')}
				</button>
			{/if}
		</div>
		{#if exportError}<p class="error">{exportError}</p>{/if}
	</section>
{/if}

<style>
	.head {
		display: flex;
		justify-content: space-between;
		align-items: flex-end;
		gap: 1rem;
		flex-wrap: wrap;
		margin-bottom: 0.25rem;
	}
	.back {
		font-size: 0.875rem;
		text-decoration: none;
	}
	h1 {
		margin-top: 0.25rem;
	}
	.day {
		margin-bottom: 1rem;
	}
	.controls,
	.date,
	.bar,
	.export-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
	}
	.date {
		gap: 0.35rem;
	}
	.date button {
		padding: 0.45rem 0.75rem;
	}
	.date input {
		width: auto;
	}
	.controls select {
		width: auto;
	}
	/* Wie die Live-Einzelansicht: so groß wie möglich, ohne die Zeitleiste aus dem Bild zu schieben */
	.stage {
		position: relative;
		left: 50%;
		translate: -50%;
		width: min(calc(100vw - 3rem), calc((100dvh - 27rem) * 16 / 9));
		min-width: min(100%, 20rem);
		padding: 0;
		overflow: hidden;
	}
	.empty {
		aspect-ratio: 16 / 9;
		display: grid;
		place-items: center;
		color: var(--text-muted);
		text-align: center;
		padding: 1rem;
	}
	.bar {
		margin: 1rem 0;
	}
	.play {
		min-width: 6rem;
	}
	.clock {
		font-variant-numeric: tabular-nums;
		font-size: 1.1rem;
		min-width: 6rem;
	}
	.segmented {
		display: inline-flex;
		border: 1px solid var(--border);
		border-radius: 10px;
		overflow: hidden;
	}
	.segmented button {
		border-radius: 0;
		padding: 0.5rem 0.8rem;
		background: transparent;
		color: var(--text-muted);
	}
	.segmented button.active {
		background: var(--accent);
		color: #fff;
	}
	.timeline {
		margin-bottom: 1rem;
	}
	.export h2 {
		font-size: 1.05rem;
	}
	.error {
		color: var(--danger);
		margin: 0.75rem 0 0;
	}
</style>
