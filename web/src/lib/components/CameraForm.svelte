<script lang="ts">
	// Formular für Kameradaten, genutzt beim Anlegen und Bearbeiten
	import { untrack } from 'svelte';
	import { slide } from 'svelte/transition';
	import {
		api,
		ApiError,
		type CameraInput,
		type MotionDetection,
		type RecordingMode,
		type StreamProbeResult
	} from '#lib/api.js';
	import { t, tError } from '#lib/i18n/index.svelte.js';
	import Field from './Field.svelte';
	import MotionZoneEditor from './MotionZoneEditor.svelte';

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
		onvifAddress: start.onvifAddress ?? '',
		preRollSeconds: String(start.preRollSeconds),
		postRollSeconds: String(start.postRollSeconds),
		quotaGb: String(start.quotaGb),
		maxRetentionDays: start.maxRetentionDays?.toString() ?? ''
	});

	// Erweiterter Modus: Erkennung nur in markierten Bereichen
	let zonesEnabled = $state(start.motionZones !== null);

	let errors = $state<Record<string, string>>({});
	let error = $state<string>();
	let busy = $state(false);
	let testing = $state(false);
	// Ergebnis je Stream, damit klar ist, welche Auflösung zu welcher Adresse gehört
	let tests = $state<{ stream: 'main' | 'sub'; result: StreamProbeResult }[]>([]);

	function toInput(): CameraInput {
		return {
			...form,
			subStreamUrl: form.subStreamUrl.trim() || null,
			username: form.username.trim() || null,
			// Beim Bearbeiten bedeutet ein leeres Feld: Passwort behalten
			password: form.password || (cameraId && hasPassword ? null : ''),
			onvifAddress: form.onvifAddress.trim() || null,
			preRollSeconds: Number(form.preRollSeconds || 0),
			postRollSeconds: Number(form.postRollSeconds || 0),
			motionZones: zonesEnabled ? form.motionZones : null,
			quotaGb: Number(form.quotaGb),
			maxRetentionDays: form.maxRetentionDays ? Number(form.maxRetentionDays) : null
		};
	}

	async function probe(url: string): Promise<StreamProbeResult> {
		try {
			return await api<StreamProbeResult>('/api/cameras/test-stream', {
				method: 'POST',
				body: {
					url,
					username: form.username || null,
					password: form.password || null,
					cameraId: cameraId ?? null
				}
			});
		} catch (e) {
			return {
				success: false,
				codec: null,
				width: 0,
				height: 0,
				error: e instanceof ApiError ? (e.field('url') ?? e.code) : 'network'
			};
		}
	}

	/** Prüft Haupt- und (falls angegeben) Substream gleichzeitig. */
	async function testStreams() {
		testing = true;
		tests = [];
		const streams: { stream: 'main' | 'sub'; url: string }[] = [
			{ stream: 'main', url: form.mainStreamUrl.trim() }
		];
		if (form.subStreamUrl.trim()) streams.push({ stream: 'sub', url: form.subStreamUrl.trim() });
		tests = await Promise.all(
			streams.map(async ({ stream, url }) => ({ stream, result: await probe(url) }))
		);
		testing = false;
	}

	// H.265 ohne H.264-Substream: Aufnahme klappt, die Live-Ansicht aber in vielen Browsern nicht
	const hevcWithoutLive = $derived(
		tests.length > 0 &&
			tests.every((x) => !x.result.success || x.result.codec === 'hevc') &&
			tests.some((x) => x.result.codec === 'hevc')
	);

	const modes: RecordingMode[] = ['Continuous', 'Motion', 'Off'];
	const detections: MotionDetection[] = ['Video', 'Onvif'];

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
			onclick={testStreams}
			disabled={testing || !form.mainStreamUrl}
		>
			{testing ? t('cameras.testing') : t('cameras.test')}
		</button>
		{#each tests as { stream, result } (stream)}
			<p class={result.success ? 'ok' : 'error'} transition:slide={{ duration: 200 }}>
				<strong>{stream === 'main' ? t('cameras.mainShort') : t('cameras.subShort')}:</strong>
				{result.success
					? t('cameras.testOk', {
							codec: (result.codec ?? '?').toUpperCase(),
							width: result.width,
							height: result.height
						})
					: tError(result.error ?? undefined)}
			</p>
		{/each}
		{#if hevcWithoutLive}
			<p class="muted small">{t('cameras.hevcHint')}</p>
		{/if}
	</div>

	<div class="field">
		<label for="rotation">{t('cameras.rotation')}</label>
		<select id="rotation" bind:value={form.rotation}>
			{#each [0, 90, 180, 270] as degrees (degrees)}
				<option value={degrees}>{degrees === 0 ? t('cameras.rotationNone') : `${degrees}°`}</option>
			{/each}
		</select>
		<p class="muted small">{t('cameras.rotationHint')}</p>
	</div>

	<fieldset class="modes">
		<legend>{t('cameras.recording')}</legend>
		{#each modes as mode (mode)}
			<label class="toggle" class:selected={form.recordingMode === mode}>
				<input type="radio" name="recordingMode" value={mode} bind:group={form.recordingMode} />
				<span>
					<strong>{t(`cameras.mode${mode}`)}</strong>
					<span class="muted small">{t(`cameras.mode${mode}Hint`)}</span>
				</span>
			</label>
		{/each}
		{#if errors.recordingMode}<p class="error small">{errors.recordingMode}</p>{/if}
	</fieldset>

	{#if form.recordingMode === 'Motion'}
		<div transition:slide={{ duration: 200 }}>
			<fieldset class="modes">
				<legend>{t('cameras.detection')}</legend>
				{#each detections as detection (detection)}
					<label class="toggle" class:selected={form.motionDetection === detection}>
						<input
							type="radio"
							name="motionDetection"
							value={detection}
							bind:group={form.motionDetection}
						/>
						<span>
							<strong>{t(`cameras.detection${detection}`)}</strong>
							<span class="muted small">{t(`cameras.detection${detection}Hint`)}</span>
						</span>
					</label>
				{/each}
			</fieldset>
			{#if form.motionDetection === 'Video'}
				<div class="field">
					<label for="sensitivity">
						{t('cameras.sensitivity')}: <strong>{form.motionSensitivity}</strong>
					</label>
					<input
						id="sensitivity"
						type="range"
						min="1"
						max="100"
						step="1"
						bind:value={form.motionSensitivity}
					/>
					<p class="muted small">{t('cameras.sensitivityHint')}</p>
					{#if errors.motionSensitivity}<p class="error small">{errors.motionSensitivity}</p>{/if}
				</div>
				<label class="toggle" class:selected={zonesEnabled}>
					<input type="checkbox" bind:checked={zonesEnabled} />
					<span>
						<strong>{t('cameras.zones')}</strong>
						<span class="muted small">{t('cameras.zonesHint')}</span>
					</span>
				</label>
				{#if zonesEnabled}
					<div transition:slide={{ duration: 200 }}>
						<MotionZoneEditor bind:value={form.motionZones} {cameraId} />
						{#if errors.motionZones}<p class="error small">{errors.motionZones}</p>{/if}
					</div>
				{/if}
			{:else}
				<Field
					label={t('cameras.onvifAddress')}
					bind:value={form.onvifAddress}
					placeholder="http://192.168.1.20:8080"
					hint={t('cameras.onvifAddressHint')}
					spellcheck="false"
					error={errors.onvifAddress ?? errors.recordingMode}
				/>
			{/if}
			<div class="row">
				<Field
					label={t('cameras.preRoll')}
					type="number"
					min="0"
					max="60"
					step="1"
					bind:value={form.preRollSeconds}
					hint={t('cameras.preRollHint')}
					error={errors.preRollSeconds}
				/>
				<Field
					label={t('cameras.postRoll')}
					type="number"
					min="0"
					max="600"
					step="1"
					bind:value={form.postRollSeconds}
					hint={t('cameras.postRollHint')}
					error={errors.postRollSeconds}
				/>
			</div>
		</div>
	{/if}

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
	.test,
	.field {
		margin-bottom: 1.25rem;
	}
	.field p {
		margin: 0.35rem 0 0;
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
		margin-top: 0.25rem;
	}
	.modes {
		border: 0;
		padding: 0;
		margin: 0 0 1.25rem;
	}
	.modes legend {
		font-weight: 600;
		margin-bottom: 0.5rem;
	}
	.modes .toggle {
		margin-bottom: 0.5rem;
	}
	.toggle.selected {
		border-color: var(--accent);
		background: color-mix(in srgb, var(--accent) 8%, transparent);
	}
	.error {
		color: var(--danger);
	}
	.toggle span {
		display: flex;
		flex-direction: column;
	}
</style>
