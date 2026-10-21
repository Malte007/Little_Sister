<script lang="ts">
	import { invalidate } from '$app/navigation';
	import { flip } from 'svelte/animate';
	import { fade, slide } from 'svelte/transition';
	import { api, ApiError } from '#lib/api.js';
	import Field from '#lib/components/Field.svelte';
	import { i18n, t, tError } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	let showForm = $state(false);
	const form = $state({ userName: '', password: '', role: 'Viewer' });
	let errors = $state<Record<string, string>>({});
	let error = $state<string>();
	let busy = $state(false);

	const dateFormat = $derived(new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium' }));

	async function reload() {
		await invalidate((url) => url.pathname === '/api/users');
	}

	async function create(event: SubmitEvent) {
		event.preventDefault();
		busy = true;
		errors = {};
		error = undefined;
		try {
			await api('/api/users', { method: 'POST', body: form });
			form.userName = '';
			form.password = '';
			showForm = false;
			await reload();
		} catch (e) {
			if (e instanceof ApiError) {
				errors = {
					userName: e.field('userName') ? tError(e.field('userName')) : '',
					password: e.field('password') ? tError(e.field('password')) : ''
				};
				if (!errors.userName && !errors.password) error = tError(e.code);
			} else error = t('errors.network');
		} finally {
			busy = false;
		}
	}

	async function remove(id: string, name: string) {
		if (!confirm(t('users.deleteConfirm', { name }))) return;
		try {
			await api(`/api/users/${id}`, { method: 'DELETE' });
			await reload();
		} catch (e) {
			error = e instanceof ApiError ? tError(e.code) : t('errors.network');
		}
	}
</script>

<div class="head">
	<div>
		<h1>{t('users.title')}</h1>
		<p class="muted">{t('users.subtitle')}</p>
	</div>
	<button onclick={() => (showForm = !showForm)}
		>{showForm ? t('common.cancel') : t('users.add')}</button
	>
</div>

{#if error}
	<div class="alert" role="alert" transition:fade>{error}</div>
{/if}

{#if showForm}
	<form class="card add" onsubmit={create} transition:slide={{ duration: 250 }}>
		<Field
			label={t('users.userName')}
			bind:value={form.userName}
			required
			error={errors.userName}
		/>
		<Field
			label={t('users.password')}
			type="password"
			bind:value={form.password}
			autocomplete="new-password"
			required
			hint={t('setup.passwordHint')}
			error={errors.password}
		/>
		<div class="field">
			<label for="role">{t('users.role')}</label>
			<select id="role" bind:value={form.role}>
				<option value="Viewer">{t('roles.Viewer')}</option>
				<option value="Admin">{t('roles.Admin')}</option>
			</select>
		</div>
		<button type="submit" disabled={busy}>{t('common.save')}</button>
	</form>
{/if}

<ul class="card list">
	{#each data.users as user (user.id)}
		<li animate:flip={{ duration: 250 }} transition:fade={{ duration: 200 }}>
			<div>
				<strong>{user.userName}</strong>
				{#if user.id === data.user?.id}<span class="badge">{t('users.you')}</span>{/if}
				{#if user.lockedOut}<span class="badge warn">{t('users.lockedOut')}</span>{/if}
				<div class="muted small">
					{t(`roles.${user.role}`)} · {t('users.createdAt')}
					{dateFormat.format(new Date(user.createdAtUtc))}
				</div>
			</div>
			{#if user.id !== data.user?.id}
				<button class="danger" onclick={() => remove(user.id, user.userName)}
					>{t('common.delete')}</button
				>
			{/if}
		</li>
	{/each}
</ul>

<style>
	.head {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 1rem;
		margin-bottom: 1.5rem;
		flex-wrap: wrap;
	}
	.add {
		margin-bottom: 1.5rem;
		max-width: 28rem;
	}
	.field {
		margin-bottom: 1.1rem;
	}
	.list {
		list-style: none;
		margin: 0;
		padding: 0.5rem 0;
	}
	li {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 1rem;
		padding: 0.9rem 1.5rem;
	}
	li + li {
		border-top: 1px solid var(--border);
	}
	li button {
		padding: 0.4rem 0.9rem;
	}
	.small {
		font-size: 0.85rem;
	}
	.badge {
		font-size: 0.75rem;
		padding: 0.1rem 0.5rem;
		border-radius: 999px;
		margin-left: 0.4rem;
		background: color-mix(in srgb, var(--accent) 18%, transparent);
		color: var(--accent);
	}
	.badge.warn {
		background: color-mix(in srgb, var(--danger) 15%, transparent);
		color: var(--danger);
	}
</style>
