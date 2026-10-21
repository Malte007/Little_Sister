<script lang="ts">
	// Zeitleiste eines Tages: aufgenommene Bereiche, Ereignismarker, Abspielposition und Export-Auswahl.
	// Klick springt an die Stelle; im vergrößerten Ausschnitt lässt sich die Leiste ziehen.
	import { untrack } from 'svelte';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import { tickStep, ticks, zoneOffset, type Range } from '#lib/playback/timeline.js';

	let {
		from,
		to,
		ranges,
		events = [],
		position,
		selection = null,
		timeZone,
		onseek
	}: {
		/** Tagesanfang und -ende in ms seit 1970 */
		from: number;
		to: number;
		/** Aufgenommene Bereiche */
		ranges: Range[];
		/** Ereignisse (z. B. Bewegung) als Zeitpunkte */
		events?: { time: number; kind: string }[];
		/** Aktuelle Abspielposition (Uhrzeit) */
		position: number | null;
		/** Für den Export markierter Bereich */
		selection?: { start: number | null; end: number | null } | null;
		timeZone: string;
		onseek: (wall: number) => void;
	} = $props();

	// Sichtbarer Ausschnitt: ganzer Tag bis 15 Minuten
	const zooms = [0, 6 * 3_600_000, 3_600_000, 15 * 60_000];
	let zoom = $state(0);
	let viewStart = $state(0);
	const daySpan = $derived(to - from);
	const span = $derived(zooms[zoom] === 0 ? daySpan : Math.min(zooms[zoom], daySpan));
	const viewEnd = $derived(viewStart + span);

	let width = $state(0);
	let hover = $state<number | null>(null);

	function clampView(start: number) {
		return Math.min(Math.max(start, from), to - span);
	}

	// Hat der Benutzer den Ausschnitt verschoben, folgt er der Position erst nach dem nächsten Sprung wieder
	let panned = false;

	// Neuer Tag oder neue Vergrößerung: Ausschnitt um die Abspielposition legen
	$effect(() => {
		const size = span;
		const center = untrack(() => position) ?? from;
		panned = false;
		viewStart = untrack(() => clampView(center - size / 2));
	});

	// Läuft die Position aus dem Ausschnitt, wandert er mit
	$effect(() => {
		if (position === null || panned) return;
		const p = position;
		untrack(() => {
			if (p < viewStart || p > viewStart + span) viewStart = clampView(p - span * 0.1);
		});
	});

	const pct = (time: number) => ((time - viewStart) / span) * 100;
	const timeAt = (x: number) => viewStart + (x / Math.max(width, 1)) * span;

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

	// Ziehen verschiebt den Ausschnitt, ein einfacher Klick springt
	let drag: { x: number; start: number; moved: boolean } | null = null;

	function pointerDown(e: PointerEvent) {
		drag = { x: e.clientX, start: viewStart, moved: false };
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function pointerMove(e: PointerEvent) {
		const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
		hover = timeAt(e.clientX - rect.left);
		if (!drag) return;
		const dx = e.clientX - drag.x;
		if (Math.abs(dx) > 4) drag.moved = true;
		if (drag.moved && span < daySpan) {
			panned = true;
			viewStart = clampView(drag.start - (dx / width) * span);
		}
	}

	function pointerUp(e: PointerEvent) {
		if (drag && !drag.moved) {
			const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
			panned = false;
			onseek(timeAt(e.clientX - rect.left));
		}
		drag = null;
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
				onclick={() => (zoom = Math.max(zoom - 1, 0))}
				disabled={zoom === 0}
				aria-label={t('playback.zoomOut')}>−</button
			>
			<span class="muted">{zoom === 0 ? t('playback.wholeDay') : t(`playback.zoom${zoom}`)}</span>
			<button
				class="secondary"
				onclick={() => (zoom = Math.min(zoom + 1, zooms.length - 1))}
				disabled={zoom === zooms.length - 1}
				aria-label={t('playback.zoomIn')}>+</button
			>
		</div>
		{#if hover !== null}
			<span class="muted hover-time">{withSeconds.format(hover)}</span>
		{/if}
	</div>

	<div
		class="track"
		role="slider"
		tabindex="0"
		aria-label={t('playback.timeline')}
		aria-valuemin={from}
		aria-valuemax={to}
		aria-valuenow={position ?? from}
		aria-valuetext={position !== null ? withSeconds.format(position) : ''}
		bind:clientWidth={width}
		onpointerdown={pointerDown}
		onpointermove={pointerMove}
		onpointerup={pointerUp}
		onpointerleave={() => (hover = null)}
		onkeydown={keydown}
	>
		{#each ranges as r (r.start)}
			{#if visible(r.start, r.end)}
				<div
					class="range"
					style:left="{Math.max(pct(r.start), 0)}%"
					style:right="{Math.max(100 - pct(r.end), 0)}%"
				></div>
			{/if}
		{/each}
		{#if sel && visible(sel.start, sel.end)}
			<div
				class="selection"
				style:left="{Math.max(pct(sel.start), 0)}%"
				style:right="{Math.max(100 - pct(sel.end), 0)}%"
			></div>
		{/if}
		{#each events as ev (ev.time)}
			{#if visible(ev.time, ev.time)}
				<div class="event" style:left="{pct(ev.time)}%" title={withSeconds.format(ev.time)}></div>
			{/if}
		{/each}
		{#if hover !== null}
			<div class="hover" style:left="{pct(hover)}%"></div>
		{/if}
		{#if position !== null && visible(position, position)}
			<div class="playhead" style:left="{pct(position)}%"></div>
		{/if}
	</div>

	<div class="ticks" aria-hidden="true">
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
	.hover-time {
		font-variant-numeric: tabular-nums;
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
	.range {
		position: absolute;
		top: 0.6rem;
		bottom: 0.6rem;
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
	.event {
		position: absolute;
		top: 0.2rem;
		width: 0.5rem;
		height: 0.5rem;
		margin-left: -0.25rem;
		border-radius: 50%;
		background: var(--danger);
	}
	.hover {
		position: absolute;
		top: 0;
		bottom: 0;
		width: 1px;
		background: var(--text-muted);
		pointer-events: none;
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
	}
	.ticks span {
		position: absolute;
		translate: -50%;
		white-space: nowrap;
	}
</style>
