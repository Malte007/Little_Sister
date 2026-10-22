<script lang="ts">
	// Kachel im Live-Raster: Bild, Name und Aufnahmezustand; Klick öffnet die Einzelansicht
	import type { Camera } from '#lib/api.js';
	import { statusOf } from '#lib/live/status.svelte.js';
	import LiveVideo from './LiveVideo.svelte';
	import StatusPill from './StatusPill.svelte';

	let { camera }: { camera: Camera } = $props();
	const status = $derived(statusOf(camera));
</script>

<a class="tile card" href="/live/{camera.id}">
	<LiveVideo cameraId={camera.id} rotation={camera.rotation} onlyWhenVisible />
	<div class="caption">
		<strong>{camera.name}</strong>
		<StatusPill state={camera.recordingEnabled ? status.state : 'Idle'} motion={status.motion} />
	</div>
</a>

<style>
	.tile {
		display: block;
		padding: 0;
		overflow: hidden;
		color: inherit;
		text-decoration: none;
		transition:
			transform 0.2s var(--ease),
			border-color 0.2s;
	}
	.tile:hover {
		transform: translateY(-2px);
		border-color: var(--accent);
	}
	.caption {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
		padding: 0.7rem 1rem;
	}
	strong {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
</style>
