<script lang="ts">
	import { fly } from 'svelte/transition';
	import { i18n, t } from '#lib/i18n/index.svelte.js';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	// Sprache der Installation gilt, solange auf diesem Gerät keine eigene gewählt wurde
	$effect(() => i18n.useDefault(data.info.language));

	const role = $derived(data.user?.roles.includes('Admin') ? 'Admin' : 'Viewer');
</script>

<section in:fly={{ y: 12, duration: 400 }}>
	<h1>{t('home.greeting', { name: data.user?.userName ?? '' })}</h1>
	<p class="muted">{t('home.signedInAs', { role: t(`roles.${role}`) })}</p>

	<div class="card empty">
		<div class="lens" aria-hidden="true"></div>
		<h2>{t('home.camerasTitle')}</h2>
		<p class="muted">{t('home.camerasText')}</p>
	</div>

	<p class="muted version">{t('home.version', { version: data.info.version })}</p>
</section>

<style>
	h1 {
		font-size: 1.8rem;
	}
	.empty {
		margin-top: 2rem;
		text-align: center;
		padding: 3.5rem 2rem;
	}
	.empty p {
		max-width: 32rem;
		margin: 0 auto;
	}
	.lens {
		width: 4rem;
		height: 4rem;
		margin: 0 auto 1.25rem;
		border-radius: 50%;
		border: 3px solid var(--accent);
		position: relative;
		animation: pulse 2.8s var(--ease) infinite;
	}
	.lens::after {
		content: '';
		position: absolute;
		inset: 1.2rem;
		border-radius: 50%;
		background: var(--accent);
	}
	@keyframes pulse {
		0%,
		100% {
			box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 40%, transparent);
		}
		50% {
			box-shadow: 0 0 0 14px transparent;
		}
	}
	.version {
		margin-top: 2rem;
		font-size: 0.8rem;
	}
</style>
