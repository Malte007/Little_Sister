<script lang="ts">
	// Wiedergabe einer oder mehrerer Kameras über eine gemeinsame Zeitleiste: Tag wählen, springen,
	// Kameras hinzufügen oder wechseln, Zeitraum als MP4 exportieren.
	// Die Uhrzeit gibt die führende Kamera vor (siehe sync.ts), die anderen folgen ihr.
	import { onMount, untrack } from 'svelte';
	import { goto, invalidate } from '$app/navigation';
	import PlaybackVideo from '#lib/components/PlaybackVideo.svelte';
	import Timeline from '#lib/components/Timeline.svelte';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import {
		buildMap,
		eventNeighbor,
		mergeRanges,
		toMedia,
		toWall,
		mediaLength
	} from '#lib/playback/timeline.js';
	import {
		jumpTolerance,
		leaderAt,
		maxCameras,
		mediaAt,
		nextRecording,
		settleJump,
		thumbnailAt
	} from '#lib/playback/sync.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const cams = $derived(data.selected);
	const days = $derived(data.days);
	const day = $derived(days[0]);
	const isToday = $derived(day ? day.date === day.today : false);
	const from = $derived(day ? Date.parse(day.fromUtc) : 0);
	const to = $derived(day ? Date.parse(day.toUtc) : 0);
	const maps = $derived(days.map((d) => buildMap(d.segments)));
	// Bewegungen je Kamera; eine laufende reicht bis jetzt
	const events = $derived(
		days.map((d) =>
			d.events.map((e) => ({
				start: Date.parse(e.startUtc),
				end: e.endUtc ? Date.parse(e.endUtc) : Date.now()
			}))
		)
	);
	const eventStarts = $derived(events.flat().map((e) => e.start));
	const lanes = $derived(
		cams.map((c, i) => ({
			label: c.name,
			ranges: mergeRanges(maps[i] ?? []),
			events: events[i] ?? []
		}))
	);
	const hasRecordings = $derived(maps.some((m) => m.length > 0));
	// Blättern zum nächstgelegenen Tag, an dem irgendeine der Kameras etwas aufgenommen hat
	const previousDate = $derived(
		days
			.map((d) => d.previousDate)
			.reduce<string | null>((a, b) => (b && (!a || b > a) ? b : a), null)
	);
	const nextDate = $derived(
		days.map((d) => d.nextDate).reduce<string | null>((a, b) => (b && (!a || b < a) ? b : a), null)
	);
	const unselected = $derived(data.cameras.filter((c) => !cams.some((s) => s.id === c.id)));
	const playlistOf = (id: string) => `/api/recordings/${id}/day/${day?.date}/index.m3u8`;

	// Kachelraster: 1, 2×1, 2×2, 3×2, 3×3
	const columns = $derived(cams.length <= 1 ? 1 : cams.length <= 4 ? 2 : 3);
	const rows = $derived(Math.ceil(cams.length / columns));

	const players: Record<string, ReturnType<typeof PlaybackVideo> | undefined> = $state({});
	let playing = $state(true);
	let rate = $state(1);
	let audioCamera = $state<string | null>(null);
	/** Aktuelle Uhrzeit der Wiedergabe (ms seit 1970) */
	let position = $state<number | null>(null);
	/** Index der Kamera, die gerade die Uhrzeit vorgibt */
	let leader = -1;
	// Neuaufbau der Player, z. B. wenn heute neue Aufnahmen dazukommen
	let reload = $state(0);

	// Startzeit bei neuem Tag: gewünschte Uhrzeit, heute die letzten fünf Minuten, sonst die erste Aufnahme.
	// Das minütliche Auffrischen und das Ändern der Kameraauswahl lassen sie unverändert.
	// Läuft vor dem Aufbau der Player, damit diese gleich an der richtigen Stelle beginnen.
	$effect.pre(() => {
		void day?.date;
		untrack(() => {
			const at = data.at ? Date.parse(data.at) : NaN;
			const latest = Math.max(...maps.map((m) => m.at(-1)?.end ?? -Infinity));
			const wanted = !Number.isNaN(at)
				? at
				: isToday && Number.isFinite(latest)
					? latest - 300_000
					: from;
			position = leaderAt(maps, wanted) >= 0 ? wanted : nextRecording(maps, wanted);
			leader = position === null ? -1 : leaderAt(maps, position);
		});
	});

	/** Startposition eines neu erzeugten Players passend zur aktuellen Uhrzeit. */
	function startOf(i: number): number {
		const map = maps[i] ?? [];
		return position === null ? 0 : (toMedia(map, position) ?? mediaLength(map));
	}

	/** Bringt alle Kameras auf die aktuelle Uhrzeit; force = sofort springen (z. B. nach einem Klick). */
	function sync(force: boolean) {
		if (position === null) return;
		let next = leaderAt(maps, position);
		if (next < 0) {
			// Keine Kamera hat hier eine Aufnahme: zur nächsten springen
			const later = nextRecording(maps, position);
			if (later === null) return;
			position = later;
			next = leaderAt(maps, later);
			force = true;
		}
		leader = next;
		const tolerance = force ? 0.3 : Math.max(1, rate * 0.5);
		cams.forEach((c, i) => players[c.id]?.follow(mediaAt(maps[i], position!), tolerance));
	}

	function onTime(id: string, media: number) {
		const i = cams.findIndex((c) => c.id === id);
		if (i < 0 || (leader >= 0 && i !== leader)) return;
		const wall = toWall(maps[i], media);
		if (wall === null) return;
		// Springt die führende Kamera über eine Lücke, prüfen, ob andere Kameras dazwischen aufnehmen
		position =
			position !== null && leader === i && wall - position > jumpTolerance(rate)
				? settleJump(maps, i, position, wall)
				: wall;
		sync(false);
	}

	function seekWall(wall: number) {
		position = wall;
		sync(true);
	}

	// Heute wächst die Aufnahme weiter: Zeitleiste jede Minute auffrischen
	onMount(() => {
		const timer = setInterval(() => {
			if (isToday && !document.hidden) invalidate('app:recording-day');
		}, 60_000);
		return () => clearInterval(timer);
	});

	// Video der führenden Kamera zu Ende: mit einer anderen Kamera weiter oder heute neue Segmente holen
	async function ended(id: string) {
		const i = cams.findIndex((c) => c.id === id);
		if (i !== leader || position === null) return;
		const other = nextRecording(maps, position + 1, i);
		if (other !== null) {
			position = other;
			sync(true);
			return;
		}
		if (!isToday) {
			playing = false;
			return;
		}
		const last = position;
		await invalidate('app:recording-day');
		const next = nextRecording(maps, last + 1);
		if (next === null) {
			playing = false;
			return;
		}
		position = next;
		playing = true;
		reload++;
	}

	/** Neue Kameraauswahl oder neuer Tag; beim Wechsel der Kameras bleibt die Uhrzeit erhalten. */
	function navigate(ids: string[], date = day?.date, keepTime = true) {
		const params = new URLSearchParams({ cameras: ids.join(',') });
		if (date) params.set('date', date);
		if (keepTime && position !== null) params.set('t', new Date(position).toISOString());
		goto(`?${params}`, { reset: false });
	}
	const ids = $derived(cams.map((c) => c.id));
	const changeDate = (date: string | null) => date && navigate(ids, date, false);
	const addCamera = (id: string) => id && navigate([...ids, id]);
	const swapCamera = (index: number, id: string) =>
		navigate(ids.map((c, i) => (i === index ? id : c)));
	const removeCamera = (id: string) => {
		if (audioCamera === id) audioCamera = null;
		navigate(ids.filter((c) => c !== id));
	};

	// Export: Anfang und Ende an der aktuellen Position setzen oder auf der Zeitleiste ziehen
	let selection = $state<{ start: number | null; end: number | null }>({ start: null, end: null });
	const maxExport = 24 * 3_600_000;
	const exportError = $derived.by(() => {
		const { start: s, end: e } = selection;
		if (s === null || e === null) return null;
		if (e <= s) return t('playback.endBeforeStart');
		if (e - s > maxExport) return t('playback.exportTooLong');
		return null;
	});
	const exportQuery = $derived(
		selection.start !== null && selection.end !== null && !exportError
			? `fromUtc=${new Date(selection.start).toISOString()}&toUtc=${new Date(selection.end).toISOString()}`
			: null
	);

	// Neuer Tag: Auswahl verwerfen
	$effect(() => {
		void day?.date;
		selection = { start: null, end: null };
	});

	const clock = $derived(
		new Intl.DateTimeFormat(i18n.language, {
			timeZone: day?.timeZoneId,
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit'
		})
	);
	const longDate = $derived(
		new Intl.DateTimeFormat(i18n.language, {
			timeZone: day?.timeZoneId,
			weekday: 'long',
			day: 'numeric',
			month: 'long',
			year: 'numeric'
		})
	);
	// Frühester Tag, den die Datumsauswahl anbietet
	const firstDate = $derived.by(() => {
		const oldest = cams
			.map((c) => c.storage.oldestUtc)
			.filter((o): o is string => !!o)
			.sort()[0];
		return oldest && day
			? new Intl.DateTimeFormat('en-CA', { timeZone: day.timeZoneId }).format(Date.parse(oldest))
			: day?.date;
	});

	// Tastatur: Leertaste spielt/pausiert, sofern kein Eingabefeld aktiv ist
	function keydown(e: KeyboardEvent) {
		const target = e.target as HTMLElement;
		if (e.key !== ' ' || ['INPUT', 'SELECT', 'BUTTON', 'TEXTAREA'].includes(target.tagName)) return;
		e.preventDefault();
		playing = !playing;
	}
</script>

<svelte:window onkeydown={keydown} />

{#if !day}
	<h1>{t('playback.title')}</h1>
	<p class="muted">{t('playback.noCameras')}</p>
{:else}
	<div class="head">
		<div>
			<a class="back muted" href={cams.length === 1 ? `/live/${cams[0].id}` : '/'}>
				← {t('playback.toLive')}
			</a>
			<h1>{t('playback.title')}</h1>
		</div>
		<div class="controls">
			{#if unselected.length > 0 && cams.length < maxCameras}
				<select
					aria-label={t('playback.addCamera')}
					value=""
					onchange={(e) => addCamera(e.currentTarget.value)}
				>
					<option value="" disabled>+ {t('playback.addCamera')}</option>
					{#each unselected as c (c.id)}
						<option value={c.id}>{c.name}</option>
					{/each}
				</select>
			{/if}
			<div class="date">
				<button
					class="secondary"
					disabled={!previousDate}
					onclick={() => changeDate(previousDate)}
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
					disabled={!nextDate}
					onclick={() => changeDate(nextDate)}
					aria-label={t('playback.nextDay')}>›</button
				>
			</div>
		</div>
	</div>

	<p class="muted day">{longDate.format(from)}</p>

	<div class="stage" style:--columns={columns} style:--rows={rows} style:--lanes={cams.length}>
		{#each cams as cam, i (cam.id)}
			<div class="card tile">
				<div class="tile-bar">
					<select
						aria-label={t('playback.camera')}
						value={cam.id}
						onchange={(e) => swapCamera(i, e.currentTarget.value)}
					>
						<option value={cam.id}>{cam.name}</option>
						{#each unselected as c (c.id)}
							<option value={c.id}>{c.name}</option>
						{/each}
					</select>
					{#if (maps[i]?.length ?? 0) > 0}
						<button
							class="tool audio"
							aria-pressed={audioCamera === cam.id}
							onclick={() => (audioCamera = audioCamera === cam.id ? null : cam.id)}
						>
							<span aria-hidden="true">{audioCamera === cam.id ? '🔊' : '🔇'}</span>
							{audioCamera === cam.id ? t('live.audioOn') : t('live.audioOff')}
						</button>
					{/if}
					{#if cams.length > 1}
						<button
							class="tool"
							title={t('playback.removeCamera')}
							aria-label={t('playback.removeCamera')}
							onclick={() => removeCamera(cam.id)}>✕</button
						>
					{/if}
				</div>
				{#if (maps[i]?.length ?? 0) > 0}
					{#key `${playlistOf(cam.id)}#${reload}`}
						<PlaybackVideo
							bind:this={players[cam.id]}
							src={playlistOf(cam.id)}
							start={startOf(i)}
							{rate}
							{playing}
							audio={audioCamera === cam.id}
							ontime={(m) => onTime(cam.id, m)}
							onended={() => ended(cam.id)}
							onblocked={() => (playing = false)}
						/>
					{/key}
				{:else}
					<div class="empty">
						<p>{t('playback.noRecordings')}</p>
					</div>
				{/if}
			</div>
		{/each}
	</div>

	{#if hasRecordings}
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
			{#if eventStarts.length > 0}
				<div class="segmented events" role="group" aria-label={t('playback.motion')}>
					{#each [-1, 1] as const as dir (dir)}
						{@const target = position === null ? null : eventNeighbor(eventStarts, position, dir)}
						<button
							disabled={target === null}
							onclick={() => target !== null && seekWall(target)}
							title={t(dir < 0 ? 'playback.previousEvent' : 'playback.nextEvent')}
						>
							{dir < 0 ? `‹ ${t('playback.motion')}` : `${t('playback.motion')} ›`}
						</button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}

	<div class="card timeline">
		<Timeline
			{from}
			{to}
			{lanes}
			{position}
			{selection}
			timeZone={day.timeZoneId}
			onseek={seekWall}
			onselect={(start, end) => (selection = { start, end })}
			thumbnail={day.thumbnailSeconds > 0
				? (lane, time) => thumbnailAt(maps[lane] ?? [], time, day.thumbnailSeconds)
				: undefined}
		/>
	</div>

	{#if hasRecordings}
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
				{#if exportQuery}
					<!-- Je Kamera eine eigene Datei -->
					{#each cams as cam, i (cam.id)}
						{#if (maps[i]?.length ?? 0) > 0}
							<a class="button" href="/api/recordings/{cam.id}/export?{exportQuery}" download>
								{cams.length > 1
									? t('playback.downloadFor', { name: cam.name })
									: t('playback.download')}
							</a>
						{/if}
					{/each}
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
	.date input,
	.controls select {
		width: auto;
	}
	/* So groß wie möglich, ohne die Zeitleiste aus dem Bild zu schieben; bei mehreren Kameras als Raster */
	.stage {
		position: relative;
		left: 50%;
		translate: -50%;
		/* Abzüglich Kopf, Steuerleiste und Zeitleiste (wächst mit jeder Spur) sowie der Leiste je Kachelreihe */
		--video-height: calc(
			100dvh - 26rem - max(0rem, var(--lanes) * 1.4rem - 2.6rem) - var(--rows) * 3rem
		);
		width: min(
			calc(100vw - 3rem),
			calc(var(--video-height) * 16 / 9 * var(--columns) / var(--rows))
		);
		min-width: min(100%, 20rem);
		display: grid;
		grid-template-columns: repeat(var(--columns), minmax(0, 1fr));
		gap: 0.5rem;
	}
	.tile {
		padding: 0;
		overflow: hidden;
	}
	/* Kopf jeder Kachel: Kameraauswahl, Ton, Entfernen – gut sichtbar wie die übrigen Bedienelemente */
	.tile-bar {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		padding: 0.4rem;
		background: var(--bg-input);
		border-bottom: 1px solid var(--border);
	}
	.tile-bar select {
		width: auto;
		flex: 1;
		min-width: 0;
		padding: 0.35rem 0.6rem;
		font-size: 0.9rem;
		font-weight: 600;
		background-color: var(--bg-raised);
		border-color: color-mix(in srgb, var(--text-muted) 50%, transparent);
	}
	.tool {
		padding: 0.35rem 0.7rem;
		font-size: 0.85rem;
		white-space: nowrap;
		background: var(--bg-raised);
		border-color: color-mix(in srgb, var(--text-muted) 50%, transparent);
		color: var(--text);
	}
	.tool:hover:not(:disabled) {
		background: var(--bg);
	}
	.tool.audio[aria-pressed='true'] {
		background: var(--accent);
		border-color: var(--accent);
		color: #fff;
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
		margin: 1rem 0;
	}
	.export h2 {
		font-size: 1.05rem;
	}
	.error {
		color: var(--danger);
		margin: 0.75rem 0 0;
	}
</style>
