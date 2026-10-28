<script lang="ts">
	// Export einer oder aller Kameras; Passwörter nur auf Wunsch und mit Warnung
	import { slide } from 'svelte/transition';
	import { ApiError } from '#lib/api.js';
	import { exportCameras, exportFileName } from '#lib/cameraTransfer.js';
	import { t, tError } from '#lib/i18n/index.svelte.js';

	let { ids, name }: { ids?: string[]; name: string } = $props();

	let includePasswords = $state(false);
	let busy = $state(false);
	let error = $state<string>();

	async function run() {
		busy = true;
		error = undefined;
		try {
			await exportCameras({ ids, includePasswords, fileName: exportFileName(name) });
		} catch (e) {
			error = e instanceof ApiError ? tError(e.code) : t('errors.network');
		} finally {
			busy = false;
		}
	}
</script>

<div class="export">
	<label class="check">
		<input type="checkbox" bind:checked={includePasswords} />
		<span>{t('cameras.exportPasswords')}</span>
	</label>
	{#if includePasswords}
		<p class="warning" transition:slide={{ duration: 180 }}>
			{t('cameras.exportPasswordsWarning')}
		</p>
	{/if}
	{#if error}<div class="alert">{error}</div>{/if}
	<button class="secondary" disabled={busy} onclick={run}>
		{ids ? t('cameras.export') : t('cameras.exportAll')}
	</button>
</div>

<style>
	.check {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		font-weight: 400;
		margin-bottom: 0.75rem;
		cursor: pointer;
	}
	.warning {
		font-size: 0.85rem;
		color: var(--danger);
		margin: 0 0 0.75rem;
	}
</style>
