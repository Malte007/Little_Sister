<script lang="ts">
	import { page } from '$app/state';
	import { t } from '#lib/i18n/index.svelte.js';
</script>

<main class="error-page">
	<h1>{page.status}</h1>
	<p class="muted">{page.status === 404 ? t('errors.unknown') : t('errors.network')}</p>
	<a class="button" href="/">{t('nav.overview')}</a>
</main>

<style>
	.error-page {
		min-height: 100vh;
		display: grid;
		place-content: center;
		text-align: center;
		gap: 0.5rem;
	}
</style>
