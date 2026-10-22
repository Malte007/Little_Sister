<script lang="ts">
	// Erkennungsbereiche ausmalen: Raster über dem Standbild, Felder per Klicken/Ziehen an- und ausschalten
	import { onMount } from 'svelte';
	import { ApiError, streamSnapshot, type StreamTarget } from '#lib/api.js';
	import { t, tError } from '#lib/i18n/index.svelte.js';

	const COLUMNS = 16;
	const ROWS = 12;
	const CELLS = COLUMNS * ROWS;

	let {
		value = $bindable(),
		source
	}: {
		/** 192 Zeichen '0'/'1', zeilenweise; null = ganzes Bild */
		value: string | null;
		/** Liefert die aktuell eingetragene Stream-Adresse; das Bild kommt direkt vom Stream, gespeichert oder nicht */
		source: () => StreamTarget;
	} = $props();

	// Arbeitskopie als Liste; null bedeutet "alles aktiv"
	let cells = $state<boolean[]>(
		value?.length === CELLS ? [...value].map((c) => c === '1') : Array(CELLS).fill(true)
	);
	let painting: boolean | null = null;
	let grid: HTMLDivElement;
	// Standbild: beim Öffnen und auf Knopfdruck laden (z. B. nach Ändern der Adresse)
	let snapshot = $state<string | null>(null);
	let loading = $state(false);
	let snapshotError = $state<string | null>(null);

	async function loadSnapshot() {
		const target = source();
		snapshotError = null;
		if (!target.url.trim()) {
			snapshotError = 'needUrl';
			return;
		}
		loading = true;
		try {
			const image = URL.createObjectURL(
				await streamSnapshot({ ...target, url: target.url.trim() })
			);
			if (snapshot) URL.revokeObjectURL(snapshot);
			snapshot = image;
		} catch (e) {
			snapshotError = e instanceof ApiError ? (e.field('url') ?? e.code) : 'network';
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		loadSnapshot();
		return () => {
			if (snapshot) URL.revokeObjectURL(snapshot);
		};
	});

	const activeCount = $derived(cells.filter(Boolean).length);

	function commit() {
		value = cells.every(Boolean) ? null : cells.map((c) => (c ? '1' : '0')).join('');
	}

	/** Feld unter dem Zeiger, oder -1 außerhalb des Rasters */
	function cellAt(event: PointerEvent): number {
		const box = grid.getBoundingClientRect();
		const col = Math.floor(((event.clientX - box.left) / box.width) * COLUMNS);
		const row = Math.floor(((event.clientY - box.top) / box.height) * ROWS);
		return col < 0 || col >= COLUMNS || row < 0 || row >= ROWS ? -1 : row * COLUMNS + col;
	}

	function start(event: PointerEvent) {
		const index = cellAt(event);
		if (index < 0) return;
		grid.setPointerCapture(event.pointerId);
		// Das erste Feld bestimmt, ob gemalt oder radiert wird
		painting = !cells[index];
		cells[index] = painting;
	}

	function move(event: PointerEvent) {
		if (painting === null) return;
		const index = cellAt(event);
		if (index >= 0) cells[index] = painting;
	}

	function end() {
		if (painting === null) return;
		painting = null;
		commit();
	}

	function setAll(fn: (active: boolean) => boolean) {
		cells = cells.map(fn);
		commit();
	}
</script>

<div class="editor">
	<div
		class="grid"
		bind:this={grid}
		onpointerdown={start}
		onpointermove={move}
		onpointerup={end}
		onpointercancel={end}
		role="application"
		aria-label={t('cameras.zones')}
	>
		{#if snapshot}
			<img src={snapshot} alt="" draggable="false" />
		{/if}
		<div class="cells">
			{#each cells as active, i (i)}
				<span class:active></span>
			{/each}
		</div>
	</div>
	{#if loading}
		<p class="muted small">{t('cameras.zonesLoading')}</p>
	{:else if snapshotError === 'needUrl'}
		<p class="muted small">{t('cameras.zonesNeedUrl')}</p>
	{:else if snapshotError}
		<p class="muted small">{t('cameras.zonesNoImage', { reason: tError(snapshotError) })}</p>
	{/if}
	<div class="actions">
		<button type="button" class="secondary" onclick={() => setAll(() => true)}>
			{t('cameras.zonesAll')}
		</button>
		<button type="button" class="secondary" onclick={() => setAll((a) => !a)}>
			{t('cameras.zonesInvert')}
		</button>
		<button type="button" class="secondary" onclick={loadSnapshot} disabled={loading}>
			{t('cameras.zonesReload')}
		</button>
		<span class="muted small">
			{t('cameras.zonesCount', { active: activeCount, total: CELLS })}
		</span>
	</div>
	{#if activeCount === 0}<p class="error small">{t('errors.invalidZones')}</p>{/if}
</div>

<style>
	.editor {
		margin-bottom: 1.25rem;
	}
	.grid {
		position: relative;
		aspect-ratio: 16 / 9;
		background: var(--bg-input);
		border-radius: 8px;
		overflow: hidden;
		touch-action: none;
		user-select: none;
		cursor: crosshair;
	}
	img {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
		object-fit: fill;
		pointer-events: none;
	}
	.cells {
		position: absolute;
		inset: 0;
		display: grid;
		grid-template-columns: repeat(16, 1fr);
		grid-template-rows: repeat(12, 1fr);
	}
	.cells span {
		border: 1px solid rgb(255 255 255 / 0.15);
		background: rgb(0 0 0 / 0.55);
	}
	.cells span.active {
		background: color-mix(in srgb, var(--motion) 30%, transparent);
		border-color: color-mix(in srgb, var(--motion) 60%, transparent);
	}
	.actions {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		flex-wrap: wrap;
		margin-top: 0.5rem;
	}
	.small {
		font-size: 0.8rem;
	}
	p {
		margin: 0.35rem 0 0;
	}
	.error {
		color: var(--danger);
	}
</style>
