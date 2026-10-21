<script lang="ts">
	// Zentrierte Karte für Login und Einrichtung, mit sanftem Einblenden
	import type { Snippet } from 'svelte';
	import { fly } from 'svelte/transition';
	import { t } from '#lib/i18n/index.svelte.js';
	import LanguageSwitcher from './LanguageSwitcher.svelte';
	import Logo from './Logo.svelte';

	let { children, wide = false }: { children: Snippet; wide?: boolean } = $props();
</script>

<div class="backdrop">
	<header>
		<Logo />
		<LanguageSwitcher />
	</header>
	<main class="card" class:wide in:fly={{ y: 16, duration: 450 }}>
		{@render children()}
	</main>
	<footer class="muted">{t('app.tagline')}</footer>
</div>

<style>
	.backdrop {
		min-height: 100vh;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 1.5rem;
		padding: 1.5rem;
		background:
			radial-gradient(
				60rem 40rem at 10% -10%,
				color-mix(in srgb, var(--accent) 18%, transparent),
				transparent
			),
			radial-gradient(
				50rem 30rem at 110% 110%,
				color-mix(in srgb, var(--success) 10%, transparent),
				transparent
			);
	}
	header {
		width: 100%;
		max-width: 26rem;
		display: flex;
		justify-content: space-between;
		align-items: center;
	}
	.backdrop:has(.wide) header {
		max-width: 34rem;
	}
	main {
		width: 100%;
		max-width: 26rem;
	}
	main.wide {
		max-width: 34rem;
	}
	footer {
		font-size: 0.85rem;
	}
</style>
