<script lang="ts">
	// Formular für Kameradaten, genutzt beim Anlegen und Bearbeiten
	import { untrack } from 'svelte';
	import { slide } from 'svelte/transition';
	import { api, ApiError, type CameraInput, type StreamProbeResult } from '#lib/api.js';
	import { t, tError } from '#lib/i18n/index.svelte.js';
	import Field from './Field.svelte';

	let {
		initial,
		cameraId,
		hasPassword = false,
		onsave
	}: {
		initial: CameraInput;
		cameraId?: string;
		hasPassword?: boolean;
		onsave: (input: CameraInput) => Promise<void>;
	} = $props();

	// Formularwerte als Text, damit leere Zahlenfelder möglich sind
	const start = untrack(() => initial);
	const form = $state({
		...start,
		subStreamUrl: start.subStreamUrl ?? '',
		username: start.username ?? '',
		password: start.password ?? '',
		quotaGb: String(start.quotaGb),
		maxRetentionDays: start.maxRetentionDays?.toString() ?? ''
	});

	let errors = $state<Record<string, string>>({});
	let error = $state<string>();
	let busy = $state(false);
	let testing = $state(false);
	let test = $state<StreamProbeResult>();

	function toInput(): CameraInput {
		return {
			...form,
			subStreamUrl: form.subStreamUrl.trim() || null,
			username: form.username.trim() || null,
			// Beim Bearbeiten bedeutet ein leeres Feld: Passwort behalten
			password: form.password || (cameraId && hasPassword ? null : ''),
			quotaGb: Number(form.quotaGb),
			maxRetentionDays: form.maxRetentionDays ? Number(form.maxRetentionDays) : null
		};
	}

	async function testStream() {
		testing = true;
		test = undefined;
		try {
			test = await api<StreamProbeResult>('/api/cameras/test-stream', {
				method: 'POST',
				body: {
					url: form.mainStreamUrl,
					username: form.username || null,
					password: form.password || null,
					cameraId: cameraId ?? null
				}
			});
		} catch (e) {
			test = {
				success: false,
				codec: null,
				width: 0,
				height: 0,
				error: e instanceof ApiError ? (e.field('url') ?? e.code) : 'network'
			};
		} finally {
			testing = false;
		}
	}

	async function submit(event: SubmitEvent) {
		event.preventDefault();
		busy = true;
		errors = {};
		error = undefined;
		try {
			await onsave(toInput());
		} catch (e) {
			if (e instanceof ApiError && Object.keys(e.fields).length > 0) {
				errors = Object.fromEntries(
					Object.entries(e.fields).map(([k, v]) => [
						k.charAt(0).toLowerCase() + k.slice(1),
						tError(v[0])
					])
				);
			} else error = e instanceof ApiError ? tError(e.code) : t('errors.network');
		} finally {
			busy = false;
		}
	}
</script>

<form onsubmit={submit}>
	{#if error}<div class="alert" role="alert">{error}</div>{/if}

	<Field
		label={t('cameras.name')}
		bind:value={form.name}
		placeholder={t('cameras.namePlaceholder')}
		required
		error={errors.name}
	/>
	<Field
		label={t('cameras.mainStream')}
		bind:value={form.mainStreamUrl}
		hint={t('cameras.mainStreamHint')}
		placeholder="rtsp://192.168.1.20:554/…"
		spellcheck="false"
		required
		error={errors.mainStreamUrl}
	/>
	<Field
		label={t('cameras.subStream')}
		bind:value={form.subStreamUrl}
		hint={t('cameras.subStreamHint')}
		placeholder="rtsp://…"
		spellcheck="false"
		error={errors.subStreamUrl}
	/>

	<div class="row">
		<Field
			label={t('cameras.username')}
			bind:value={form.username}
			autocomplete="off"
			spellcheck="false"
		/>
		<Field
			label={t('cameras.password')}
			type="password"
			bind:value={form.password}
			autocomplete="new-password"
			placeholder={cameraId && hasPassword ? '••••••••' : ''}
			hint={cameraId && hasPassword ? t('cameras.passwordKeep') : undefined}
		/>
	</div>

	<div class="test">
		<button
			type="button"
			class="secondary"
			onclick={testStream}
			disabled={testing || !form.mainStreamUrl}
		>
			{testing ? t('cameras.testing') : t('cameras.test')}
		</button>
		{#if test}
			<p class={test.success ? 'ok' : 'error'} transition:slide={{ duration: 200 }}>
				{test.success
					? t('cameras.testOk', {
							codec: (test.codec ?? '?').toUpperCase(),
							width: test.width,
							height: test.height
						})
					: tError(test.error ?? undefined)}
			</p>
			{#if test.success && test.codec === 'hevc'}
				<p class="muted small">{t('cameras.hevcHint')}</p>
			{/if}
		{/if}
	</div>

	<label class="toggle">
		<input type="checkbox" role="switch" bind:checked={form.recordingEnabled} />
		<span>
			<strong>{t('cameras.recording')}</strong>
			<span class="muted small"
				>{form.recordingEnabled ? t('cameras.recordingHint') : t('cameras.recordingOffHint')}</span
			>
		</span>
	</label>

	<div class="row">
		<Field
			label={t('cameras.quota')}
			type="number"
			min="1"
			step="1"
			bind:value={form.quotaGb}
			hint={t('cameras.quotaHint')}
			error={errors.quotaGb}
		/>
		<Field
			label={t('cameras.retention')}
			type="number"
			min="1"
			step="1"
			bind:value={form.maxRetentionDays}
			hint={t('cameras.retentionHint')}
			error={errors.maxRetentionDays}
		/>
	</div>

	<button type="submit" disabled={busy}>{busy ? t('common.loading') : t('cameras.save')}</button>
</form>

<style>
	.row {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 1rem;
	}
	@media (max-width: 640px) {
		.row {
			grid-template-columns: 1fr;
			gap: 0;
		}
	}
	.test {
		margin-bottom: 1.25rem;
	}
	.test p {
		margin: 0.6rem 0 0;
		font-size: 0.875rem;
	}
	.ok {
		color: var(--success);
	}
	.small {
		font-size: 0.8rem;
	}
	.toggle {
		display: flex;
		gap: 0.75rem;
		align-items: flex-start;
		font-weight: 400;
		margin-bottom: 1.25rem;
		padding: 0.9rem 1rem;
		border: 1px solid var(--border);
		border-radius: 10px;
		cursor: pointer;
	}
	.toggle input {
		margin-top: 0.1rem;
	}
	.toggle span {
		display: flex;
		flex-direction: column;
	}
</style>
