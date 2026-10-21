<script lang="ts">
	// Kachel einer Kamera: Name, Zustand, Speicherbelegung
	import type { Camera } from '#lib/api.js';
	import { formatBytes, formatDays } from '#lib/format.js';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import { statusOf } from '#lib/live/status.svelte.js';
	import StatusPill from './StatusPill.svelte';
	import StorageBar from './StorageBar.svelte';

	let { camera, editable = false }: { camera: Camera; editable?: boolean } = $props();

	const status = $derived(statusOf(camera));
	const quotaBytes = $derived(camera.quotaGb * 1024 ** 3);
	const days = $derived(formatDays(camera.storage.estimatedDays, i18n.language));
	const time = $derived(
		new Intl.DateTimeFormat(i18n.language, { dateStyle: 'short', timeStyle: 'short' })
	);
	const statusTitle = $derived(
		[
			t('status.since', { time: time.format(new Date(status.sinceUtc)) }),
			status.restarts > 0 ? t('status.restarts', { count: status.restarts }) : '',
			status.lastError ? t('status.lastError', { error: status.lastError }) : ''
		]
			.filter(Boolean)
			.join(' · ')
	);
</script>

<article class="card camera">
	<div class="head">
		<div>
			<h3>{camera.name}</h3>
			{#if camera.manufacturer || camera.model}
				<p class="muted small">{[camera.manufacturer, camera.model].filter(Boolean).join(' ')}</p>
			{/if}
		</div>
		<StatusPill state={status.state} title={statusTitle} />
	</div>

	{#if status.state === 'Error' && status.lastError}
		<p class="error small">{status.lastError}</p>
	{/if}

	<StorageBar
		value={camera.storage.usedBytes}
		max={quotaBytes}
		label={t('cameras.storageUsed', {
			used: formatBytes(camera.storage.usedBytes, i18n.language),
			quota: formatBytes(quotaBytes, i18n.language)
		}) +
			' · ' +
			(days ? t('cameras.storageDays', { days }) : t('cameras.storageDaysUnknown'))}
	/>

	{#if editable}
		<a class="edit" href="/cameras/{camera.id}">{t('cameras.edit')}</a>
	{/if}
</article>

<style>
	.camera {
		padding: 1.25rem 1.4rem;
		display: flex;
		flex-direction: column;
		gap: 0.9rem;
		transition:
			transform 0.25s var(--ease),
			border-color 0.25s;
	}
	.camera:hover {
		transform: translateY(-2px);
		border-color: color-mix(in srgb, var(--accent) 35%, var(--border));
	}
	.head {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 0.75rem;
	}
	h3 {
		margin: 0;
		font-size: 1.05rem;
	}
	.small {
		font-size: 0.8rem;
		margin: 0.15rem 0 0;
	}
	.error {
		margin: 0;
	}
	.edit {
		align-self: flex-end;
		color: var(--accent);
		text-decoration: none;
		font-size: 0.875rem;
		font-weight: 600;
	}
</style>
