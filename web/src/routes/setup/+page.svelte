<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { untrack } from 'svelte';
	import { fly } from 'svelte/transition';
	import { api, ApiError } from '#lib/api.js';
	import AuthCard from '#lib/components/AuthCard.svelte';
	import Field from '#lib/components/Field.svelte';
	import { i18n, t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	// Eingaben aller Schritte; Vorschläge kommen vom Server bzw. Browser
	// Nur die Startwerte übernehmen; danach gehören die Eingaben dem Formular
	const defaults = untrack(() => data.setup);
	const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
	const form = $state({
		userName: 'admin',
		password: '',
		passwordRepeat: '',
		recordingsPath: defaults.defaultRecordingsPath,
		language: i18n.language,
		timeZoneId: browserTimeZone || defaults.defaultTimeZoneId
	});
	// "UTC" fehlt in manchen Browsern in der Liste, deshalb die gewählte Zone immer ergänzen
	const timeZones = [...new Set([form.timeZoneId, ...Intl.supportedValuesOf('timeZone')])];

	const steps = ['stepAccount', 'stepStorage', 'stepRegion'] as const;
	let step = $state(0);
	let direction = $state(1);
	let busy = $state(false);
	let errors = $state<Record<string, string>>({});
	let generalError = $state<string>();

	/** Prüft lokal, was ohne Server prüfbar ist, damit Fehler früh auffallen. */
	function validateStep(): boolean {
		const e: Record<string, string> = {};
		if (step === 0) {
			if (!form.userName.trim()) e.userName = tError('required');
			if (form.password.length < 10) e.password = tError('PasswordTooShort');
			else if (form.password !== form.passwordRepeat)
				e.passwordRepeat = t('setup.passwordMismatch');
		}
		// Absoluter Pfad unter Linux (/…) oder Windows (C:\… bzw. \\server\…)
		if (step === 1 && !/^(\/|[A-Za-z]:[\\/]|\\\\)/.test(form.recordingsPath))
			e.recordingsPath = tError('absolutePathRequired');
		errors = e;
		return Object.keys(e).length === 0;
	}

	function go(delta: number) {
		if (delta > 0 && !validateStep()) return;
		direction = delta;
		step += delta;
	}

	/** Springt zum Schritt, in dem der Server einen Fehler gefunden hat. */
	const fieldStep: Record<string, number> = {
		username: 0,
		password: 0,
		recordingspath: 1,
		language: 2,
		timezoneid: 2
	};

	async function finish(event: SubmitEvent) {
		event.preventDefault();
		if (step < steps.length - 1) return go(1);

		busy = true;
		generalError = undefined;
		try {
			await api('/api/setup', {
				method: 'POST',
				body: {
					userName: form.userName,
					password: form.password,
					language: form.language,
					timeZoneId: form.timeZoneId,
					recordingsPath: form.recordingsPath
				}
			});
			i18n.useDefault(form.language);
			await invalidateAll();
			await goto('/');
		} catch (e) {
			if (e instanceof ApiError && Object.keys(e.fields).length > 0) {
				const mapped: Record<string, string> = {};
				for (const [field, codes] of Object.entries(e.fields)) {
					const key = field.charAt(0).toLowerCase() + field.slice(1);
					mapped[key] = tError(codes[0]);
				}
				errors = mapped;
				const first = Object.keys(e.fields)[0].toLowerCase();
				direction = -1;
				step = fieldStep[first] ?? step;
			} else {
				generalError = e instanceof ApiError ? tError(e.code) : t('errors.network');
			}
		} finally {
			busy = false;
		}
	}
</script>

<AuthCard wide>
	<p class="muted step">{t('setup.step', { current: step + 1, total: steps.length })}</p>
	<div class="progress"><span style:width="{((step + 1) / steps.length) * 100}%"></span></div>

	{#if step === 0}
		<h1>{t('setup.title')}</h1>
		<p class="muted">{t('setup.intro')}</p>
	{/if}

	{#if generalError}
		<div class="alert" role="alert">{generalError}</div>
	{/if}

	<form onsubmit={finish}>
		{#key step}
			<section in:fly={{ x: 40 * direction, duration: 350 }}>
				<h2>{t(`setup.${steps[step]}`)}</h2>
				<p class="muted">{t(`setup.${steps[step]}Text`)}</p>

				{#if step === 0}
					<Field
						label={t('setup.userName')}
						bind:value={form.userName}
						autocomplete="username"
						error={errors.userName}
					/>
					<Field
						label={t('setup.password')}
						type="password"
						bind:value={form.password}
						autocomplete="new-password"
						hint={t('setup.passwordHint')}
						error={errors.password}
					/>
					<Field
						label={t('setup.passwordRepeat')}
						type="password"
						bind:value={form.passwordRepeat}
						autocomplete="new-password"
						error={errors.passwordRepeat}
					/>
				{:else if step === 1}
					<Field
						label={t('setup.recordingsPath')}
						bind:value={form.recordingsPath}
						hint={t('setup.recordingsPathHint')}
						error={errors.recordingsPath}
						spellcheck="false"
					/>
				{:else}
					<div class="field">
						<label for="language">{t('setup.language')}</label>
						<select
							id="language"
							bind:value={form.language}
							onchange={() => i18n.set(form.language)}
						>
							{#each data.setup.languages as lang (lang)}
								<option value={lang}>{lang === 'de' ? 'Deutsch' : 'English'}</option>
							{/each}
						</select>
					</div>
					<div class="field">
						<label for="tz">{t('setup.timeZone')}</label>
						<select id="tz" bind:value={form.timeZoneId}>
							{#each timeZones as tz (tz)}
								<option value={tz}>{tz.replaceAll('_', ' ')}</option>
							{/each}
						</select>
						{#if errors.timeZoneId}<p class="error">{errors.timeZoneId}</p>{/if}
					</div>
				{/if}
			</section>
		{/key}

		<div class="actions">
			{#if step > 0}
				<button type="button" class="secondary" onclick={() => go(-1)}>{t('common.back')}</button>
			{/if}
			<button type="submit" disabled={busy}>
				{busy
					? t('common.loading')
					: step === steps.length - 1
						? t('setup.finish')
						: t('common.next')}
			</button>
		</div>
	</form>
</AuthCard>

<style>
	.step {
		font-size: 0.8rem;
		margin-bottom: 0.4rem;
		text-transform: uppercase;
		letter-spacing: 0.06em;
	}
	.progress {
		height: 4px;
		background: var(--bg-input);
		border-radius: 999px;
		overflow: hidden;
		margin-bottom: 1.5rem;
	}
	.progress span {
		display: block;
		height: 100%;
		background: var(--accent);
		transition: width 0.45s var(--ease);
	}
	h2 {
		font-size: 1.15rem;
		margin-top: 0.5rem;
	}
	section {
		min-height: 15rem;
	}
	.field {
		margin-bottom: 1.1rem;
	}
	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
		margin-top: 0.5rem;
	}
</style>
