<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api, ApiError } from '#lib/api.js';
	import AuthCard from '#lib/components/AuthCard.svelte';
	import Field from '#lib/components/Field.svelte';
	import { t, tError } from '#lib/i18n/index.svelte.js';

	let userName = $state('');
	let password = $state('');
	let rememberMe = $state(true);
	let busy = $state(false);
	let error = $state<string>();

	async function submit(event: SubmitEvent) {
		event.preventDefault();
		busy = true;
		error = undefined;
		try {
			await api('/api/auth/login', { method: 'POST', body: { userName, password, rememberMe } });
			await invalidateAll();
			await goto('/');
		} catch (e) {
			error = e instanceof ApiError ? tError(e.code) : t('errors.network');
			password = '';
		} finally {
			busy = false;
		}
	}
</script>

<AuthCard>
	<h1>{t('login.title')}</h1>
	<p class="muted">{t('login.subtitle')}</p>

	{#if error}
		<div class="alert" role="alert">{error}</div>
	{/if}

	<form onsubmit={submit}>
		<Field label={t('login.userName')} bind:value={userName} autocomplete="username" required />
		<Field
			label={t('login.password')}
			type="password"
			bind:value={password}
			autocomplete="current-password"
			required
		/>
		<label class="remember">
			<input type="checkbox" bind:checked={rememberMe} />
			{t('login.rememberMe')}
		</label>
		<button type="submit" disabled={busy}>{busy ? t('common.loading') : t('login.submit')}</button>
	</form>
</AuthCard>

<style>
	form button {
		width: 100%;
		margin-top: 0.5rem;
	}
	.remember {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-weight: 400;
		margin-bottom: 1rem;
	}
</style>
