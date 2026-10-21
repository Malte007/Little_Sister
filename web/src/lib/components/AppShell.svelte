<script lang="ts">
	// Rahmen der angemeldeten Oberfläche: Navigation oben, Inhalt darunter
	import { onMount, type Snippet } from 'svelte';
	import { goto, invalidateAll } from '$app/navigation';
	import { page } from '$app/state';
	import { api, type CurrentUser } from '#lib/api.js';
	import { t } from '#lib/i18n/index.svelte.js';
	import { connectStatus, disconnectStatus } from '#lib/live/status.svelte.js';
	import LanguageSwitcher from './LanguageSwitcher.svelte';
	import Logo from './Logo.svelte';

	let { user, children }: { user: CurrentUser; children: Snippet } = $props();
	const isAdmin = $derived(user.roles.includes('Admin'));

	// Statusmeldungen der Kameras empfangen, solange jemand angemeldet ist
	onMount(() => {
		connectStatus();
		return disconnectStatus;
	});

	async function logout() {
		disconnectStatus();
		await api('/api/auth/logout', { method: 'POST' });
		await goto('/login');
		await invalidateAll();
	}
</script>

<header>
	<a href="/" class="brand"><Logo /></a>
	<nav>
		<a href="/" class:active={page.url.pathname === '/'}>{t('nav.overview')}</a>
		<a href="/recordings" class:active={page.url.pathname.startsWith('/recordings')}
			>{t('nav.recordings')}</a
		>
		<a href="/cameras" class:active={page.url.pathname.startsWith('/cameras')}>{t('nav.cameras')}</a
		>
		{#if isAdmin}
			<a href="/users" class:active={page.url.pathname.startsWith('/users')}>{t('nav.users')}</a>
		{/if}
	</nav>
	<div class="right">
		<LanguageSwitcher />
		<span class="muted user">{user.userName}</span>
		<button class="secondary" onclick={logout}>{t('common.logout')}</button>
	</div>
</header>

<main>
	{@render children()}
</main>

<style>
	header {
		position: sticky;
		top: 0;
		z-index: 10;
		display: flex;
		align-items: center;
		gap: 1.5rem;
		padding: 0.75rem 1.5rem;
		background: color-mix(in srgb, var(--bg) 80%, transparent);
		backdrop-filter: blur(12px);
		border-bottom: 1px solid var(--border);
	}
	.brand {
		color: inherit;
		text-decoration: none;
	}
	nav {
		display: flex;
		gap: 0.25rem;
		flex: 1;
	}
	nav a {
		color: var(--text-muted);
		text-decoration: none;
		padding: 0.45rem 0.8rem;
		border-radius: 8px;
		transition:
			color 0.2s,
			background 0.2s;
	}
	nav a:hover,
	nav a.active {
		color: var(--text);
		background: var(--bg-input);
	}
	.right {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}
	.right button {
		padding: 0.45rem 0.9rem;
	}
	main {
		max-width: 72rem;
		margin: 0 auto;
		padding: 2rem 1.5rem;
	}
	@media (max-width: 640px) {
		header {
			flex-wrap: wrap;
			gap: 0.75rem;
			padding: 0.75rem 1rem;
		}
		nav {
			order: 3;
			flex-basis: 100%;
		}
		.user {
			display: none;
		}
		main {
			padding: 1.25rem 1rem;
		}
	}
</style>
