<script lang="ts">
	// Zeitleiste eines Tages: aufgenommene Bereiche und Bewegungen (je Kamera eine Spur), Abspielposition
	// und Export-Auswahl. Klick springt, Ziehen markiert einen Zeitraum, Mausrad zoomt um den Mauszeiger,
	// Ziehen auf der Skala verschiebt den Ausschnitt. Beim Überfahren erscheint ein Vorschaubild.
	import { untrack } from 'svelte';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import { tickStep, ticks, zoneOffset, type Range } from '#lib/playback/timeline.js';
	import type { Thumbnail } from '#lib/playback/sync.js';

	let {
		from,
		to,
		lanes,
		position,
		selection = null,
		timeZone,
		onseek,
		onselect,
		thumbnail
	}: {
		/** Tagesanfang und -ende in ms seit 1970 */
		from: number;
		to: number;
		/** Aufgenommene Bereiche und Bewegungen je Kamera; bei mehreren Kameras eine Spur je Kamera */
		lanes: { label: string; ranges: Range[]; events?: Range[] }[];
		/** Aktuelle Abspielposition (Uhrzeit) */
		position: number | null;
		/** Für den Export markierter Bereich */
		selection?: { start: number | null; end: number | null } | null;
		timeZone: string;
		onseek: (wall: number) => void;
		/** Mit der Maus markierter Zeitraum */
		onselect?: (start: number, end: number) => void;
		/** Vorschaubild einer Spur zur Uhrzeit */
		thumbnail?: (lane: number, time: number) => Thumbnail | null;
	} = $props();

	// Ausschnitte: ganzer Tag bis 3 Minuten
	const zooms = [0, 6 * 3_600_000, 3_600_000, 15 * 60_000, 3 * 60_000];
	const maxZoom = zooms.length - 1;

	let zoom = $state(0);
	let viewStart = $state(0);
	const daySpan = $derived(to - from);
	const spanOf = (level: number) =>
		zooms[level] === 0 ? to - from : Math.min(zooms[level], to - from);
	const span = $derived(spanOf(zoom));
	const viewEnd = $derived(viewStart + span);

	let track = $state<HTMLDivElement>();
	let width = $state(0);
	let hover = $state<{ time: number; x: number; lane: number } | null>(null);
	let height = $state(0);

	const clamp = (start: number, size: number) => Math.min(Math.max(start, from), to - size);
	const pct = (time: number) => ((time - viewStart) / span) * 100;
	const timeAt = (x: number) => viewStart + (x / Math.max(width, 1)) * span;
	const xOf = (e: PointerEvent | WheelEvent) =>
		e.clientX - (track?.getBoundingClientRect().left ?? 0);

	/** Wechselt die Vergrößerung; die Zeit unter dem Ankerpunkt (Anteil der Breite) bleibt an ihrem Platz. */
	function setZoom(level: number, anchor: number, fraction: number) {
		zoom = level;
		viewStart = clamp(anchor - fraction * spanOf(level), spanOf(level));
	}

	// Hat der Benutzer den Ausschnitt verschoben, folgt er der Position erst nach dem nächsten Sprung wieder
	let panned = false;

	// Neuer Tag: ganzer Tag sichtbar
	$effect(() => {
		void from;
		untrack(() => {
			zoom = 0;
			panned = false;
			viewStart = from;
		});
	});

	// Läuft die Position aus dem Ausschnitt, wandert er mit (nicht während des Ziehens)
	$effect(() => {
		if (position === null) return;
		const p = position;
		untrack(() => {
			if (panned || drag) return;
			if (p < viewStart || p > viewStart + span) viewStart = clamp(p - span * 0.1, span);
		});
	});

	const hourMinute = $derived(
		new Intl.DateTimeFormat(i18n.language, { timeZone, hour: '2-digit', minute: '2-digit' })
	);
	const withSeconds = $derived(
		new Intl.DateTimeFormat(i18n.language, {
			timeZone,
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit'
		})
	);
	const labels = $derived(
		ticks(viewStart, viewEnd, tickStep(span), (t) => zoneOffset(timeZone, t))
	);

	// Auf der Leiste: Klick springt, Ziehen markiert einen Zeitraum
	let drag: { x: number; time: number; moved: boolean } | null = null;

	function pointerDown(e: PointerEvent) {
		if (e.button !== 0) return;
		const x = xOf(e);
		drag = { x, time: timeAt(x), moved: false };
		track?.setPointerCapture(e.pointerId);
	}

	function pointerMove(e: PointerEvent) {
		const x = Math.min(Math.max(xOf(e), 0), width);
		const time = timeAt(x);
		const y = e.clientY - (track?.getBoundingClientRect().top ?? 0);
		const lane = Math.min(
			Math.max(Math.floor((y / Math.max(height, 1)) * lanes.length), 0),
			lanes.length - 1
		);
		hover = { time, x, lane };
		if (!drag) return;
		if (Math.abs(x - drag.x) > 4) drag.moved = true;
		if (drag.moved) onselect?.(Math.min(drag.time, time), Math.max(drag.time, time));
	}

	function pointerUp(e: PointerEvent) {
		if (drag && !drag.moved) {
			panned = false;
			onseek(timeAt(xOf(e)));
		}
		drag = null;
	}

	// Mausrad zoomt um den Mauszeiger, waagerecht (oder mit Umschalt) verschiebt es
	function wheel(e: WheelEvent) {
		e.preventDefault();
		const horizontal = e.shiftKey ? e.deltaY : e.deltaX;
		if (Math.abs(horizontal) > Math.abs(e.shiftKey ? 0 : e.deltaY)) {
			panned = true;
			viewStart = clamp(viewStart + (horizontal / Math.max(width, 1)) * span, span);
			return;
		}
		const x = xOf(e);
		const next = Math.min(Math.max(zoom + (e.deltaY < 0 ? 1 : -1), 0), maxZoom);
		if (next !== zoom) setZoom(next, timeAt(x), x / Math.max(width, 1));
	}

	// Ziehen auf der Skala verschiebt den Ausschnitt
	let pan: { x: number; start: number } | null = null;
	function panDown(e: PointerEvent) {
		pan = { x: e.clientX, start: viewStart };
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}
	function panMove(e: PointerEvent) {
		if (!pan || span >= daySpan) return;
		panned = true;
		viewStart = clamp(pan.start - ((e.clientX - pan.x) / Math.max(width, 1)) * span, span);
	}

	function zoomButton(delta: number) {
		const anchor =
			position !== null && position >= viewStart && position <= viewEnd
				? position
				: viewStart + span / 2;
		setZoom(Math.min(Math.max(zoom + delta, 0), maxZoom), anchor, 0.5);
	}

	function keydown(e: KeyboardEvent) {
		if (position === null) return;
		const step = e.shiftKey ? 60_000 : 10_000;
		if (e.key === 'ArrowLeft') onseek(Math.max(position - step, from));
		else if (e.key === 'ArrowRight') onseek(Math.min(position + step, to));
		else return;
		e.preventDefault();
	}

	const visible = (start: number, end: number) => end > viewStart && start < viewEnd;

	// Vorschaubild: Ausschnitt aus dem Bildstreifen des Segments unter dem Mauszeiger
	const preview = $derived(hover && thumbnail ? thumbnail(hover.lane, hover.time) : null);
	// Seitenverhältnis eines Bildes je Streifen, bekannt nach dem Laden (gedrehte Kameras sind hochkant)
	let aspects = $state<Record<string, number>>({});
	const previewHeight = 90;
	const previewWidth = $derived(
		previewHeight * (preview ? (aspects[preview.src] ?? 16 / 9) : 16 / 9)
	);
	const previewLeft = $derived(
		hover ? Math.min(Math.max(hover.x - previewWidth / 2, 0), Math.max(width - previewWidth, 0)) : 0
	);
	let failedThumbs = $state<Record<string, boolean>>({});
	const sel = $derived(
		selection?.start != null && selection?.end != null && selection.end > selection.start
			? { start: selection.start, end: selection.end }
			: null
	);
</script>

<div class="timeline">
	<div class="toolbar">
		<div class="zoom" role="group" aria-label={t('playback.zoom')}>
			<button
				class="secondary"
				onclick={() => zoomButton(-1)}
				disabled={zoom === 0}
				aria-label={t('playback.zoomOut')}>−</button
			>
			<span class="muted">{zoom === 0 ? t('playback.wholeDay') : t(`playback.zoom${zoom}`)}</span>
			<button
				class="secondary"
				onclick={() => zoomButton(1)}
				disabled={zoom === maxZoom}
				aria-label={t('playback.zoomIn')}>+</button
			>
		</div>
		<span class="muted help">{t('playback.timelineHelp')}</span>
	</div>

	<div class="track-wrap">
		{#if hover && preview && !failedThumbs[preview.src]}
			<div
				class="preview"
				style:left="{previewLeft}px"
				style:width="{previewWidth}px"
				style:height="{previewHeight}px"
			>
				<img
					src={preview.src}
					alt=""
					style:width="{preview.count * 100}%"
					style:translate="{(-preview.index / preview.count) * 100}% 0"
					onload={(e) => {
						const img = e.currentTarget as HTMLImageElement;
						const count = preview?.count ?? 1;
						aspects[img.src.slice(img.src.indexOf('/api/'))] =
							img.naturalWidth / count / img.naturalHeight;
					}}
					onerror={() => preview && (failedThumbs[preview.src] = true)}
				/>
				<span
					>{lanes.length > 1 ? `${lanes[hover.lane].label} · ` : ''}{withSeconds.format(
						hover.time
					)}</span
				>
			</div>
		{/if}
		<div
			class="track"
			role="slider"
			tabindex="0"
			aria-label={t('playback.timeline')}
			aria-valuemin={from}
			aria-valuemax={to}
			aria-valuenow={position ?? from}
			aria-valuetext={position !== null ? withSeconds.format(position) : ''}
			bind:this={track}
			bind:clientWidth={width}
			bind:clientHeight={height}
			style:height={lanes.length > 1 ? `${lanes.length * 1.4 + 0.4}rem` : undefined}
			onpointerdown={pointerDown}
			onpointermove={pointerMove}
			onpointerup={pointerUp}
			onpointerleave={() => (hover = null)}
			onwheel={wheel}
			onkeydown={keydown}
		>
			{#each lanes as lane, i (i)}
				<div
					class="lane"
					style:top="{(i / lanes.length) * 100}%"
					style:height="{100 / lanes.length}%"
				>
					{#each lane.ranges as r (r.start)}
						{#if visible(r.start, r.end)}
							<div
								class="range"
								style:left="{Math.max(pct(r.start), 0)}%"
								style:right="{Math.max(100 - pct(r.end), 0)}%"
							></div>
						{/if}
					{/each}
					{#each lane.events ?? [] as ev (ev.start)}
						{#if visible(ev.start, ev.end)}
							<div
								class="event"
								style:left="{Math.max(pct(ev.start), 0)}%"
								style:right="{Math.max(100 - pct(ev.end), 0)}%"
							></div>
						{/if}
					{/each}
					{#if lanes.length > 1}<span class="lane-label">{lane.label}</span>{/if}
				</div>
			{/each}
			{#if sel && visible(sel.start, sel.end)}
				<div
					class="selection"
					style:left="{Math.max(pct(sel.start), 0)}%"
					style:right="{Math.max(100 - pct(sel.end), 0)}%"
				></div>
			{/if}
			{#if position !== null && visible(position, position)}
				<div class="playhead" style:left="{pct(position)}%"></div>
			{/if}
			{#if hover !== null}
				<div class="hover" style:left="{pct(hover.time)}%">
					{#if !preview || failedThumbs[preview.src]}
						<span class:right={hover.x > width - 80}>{withSeconds.format(hover.time)}</span>
					{/if}
				</div>
			{/if}
		</div>
	</div>

	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="ticks"
		class:pannable={span < daySpan}
		aria-hidden="true"
		onpointerdown={panDown}
		onpointermove={panMove}
		onpointerup={() => (pan = null)}
	>
		{#each labels as l (l)}
			<span style:left="{pct(l)}%">{hourMinute.format(l)}</span>
		{/each}
	</div>
</div>

<style>
	.timeline {
		user-select: none;
	}
	.toolbar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		margin-bottom: 0.5rem;
		font-size: 0.85rem;
	}
	.zoom {
		display: inline-flex;
		align-items: center;
		gap: 0.5rem;
	}
	.zoom button {
		padding: 0.15rem 0.6rem;
		min-width: 2rem;
	}
	.zoom span {
		min-width: 6rem;
		text-align: center;
	}
	.help {
		font-size: 0.75rem;
		text-align: right;
	}
	.track {
		position: relative;
		height: 3rem;
		background: var(--bg-input);
		border: 1px solid var(--border);
		border-radius: 10px;
		overflow: hidden;
		cursor: pointer;
		touch-action: none;
	}
	.track:focus-visible {
		outline: 2px solid var(--accent);
		outline-offset: 2px;
	}
	.track-wrap {
		position: relative;
	}
	.lane {
		position: absolute;
		left: 0;
		right: 0;
	}
	.lane + .lane {
		border-top: 1px solid var(--border);
	}
	.lane-label {
		position: absolute;
		left: 0.4rem;
		top: 50%;
		translate: 0 -50%;
		font-size: 0.7rem;
		color: var(--text);
		text-shadow: 0 0 3px var(--bg-input);
		pointer-events: none;
		white-space: nowrap;
	}
	/* Vorschaubild über der Leiste; zeigt einen Ausschnitt aus dem Bildstreifen */
	.preview {
		position: absolute;
		bottom: calc(100% + 0.4rem);
		z-index: 2;
		overflow: hidden;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: #000;
		box-shadow: 0 4px 16px rgb(0 0 0 / 0.35);
		pointer-events: none;
	}
	.preview img {
		display: block;
		height: 100%;
		max-width: none;
	}
	.preview span {
		position: absolute;
		left: 0;
		right: 0;
		bottom: 0;
		padding: 0.1rem 0.35rem;
		font-size: 0.7rem;
		color: #fff;
		background: rgb(0 0 0 / 0.55);
		font-variant-numeric: tabular-nums;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.range {
		position: absolute;
		top: 20%;
		bottom: 20%;
		min-width: 2px;
		background: color-mix(in srgb, var(--accent) 55%, transparent);
		border-radius: 4px;
	}
	.selection {
		position: absolute;
		top: 0;
		bottom: 0;
		background: color-mix(in srgb, var(--success) 25%, transparent);
		border-left: 2px solid var(--success);
		border-right: 2px solid var(--success);
	}
	/* Bewegung als schmaler Balken oben in der Spur, auch kurze Ereignisse bleiben sichtbar */
	.event {
		position: absolute;
		top: 0;
		height: 30%;
		min-width: 3px;
		border-radius: 2px;
		background: var(--motion);
		pointer-events: none;
	}
	.hover {
		position: absolute;
		top: 0;
		bottom: 0;
		width: 1px;
		background: var(--text-muted);
		pointer-events: none;
	}
	/* Uhrzeit direkt am Mauszeiger, am rechten Rand links davon */
	.hover span {
		position: absolute;
		top: 0.1rem;
		left: 0.3rem;
		font-size: 0.7rem;
		font-variant-numeric: tabular-nums;
		color: var(--text);
		white-space: nowrap;
	}
	.hover span.right {
		left: auto;
		right: 0.3rem;
	}
	.playhead {
		position: absolute;
		top: 0;
		bottom: 0;
		width: 2px;
		margin-left: -1px;
		background: var(--text);
		pointer-events: none;
	}
	.ticks {
		position: relative;
		height: 1.25rem;
		margin-top: 0.25rem;
		font-size: 0.75rem;
		color: var(--text-muted);
		font-variant-numeric: tabular-nums;
		touch-action: none;
	}
	.ticks.pannable {
		cursor: grab;
	}
	.ticks span {
		position: absolute;
		translate: -50%;
		white-space: nowrap;
	}
</style>
