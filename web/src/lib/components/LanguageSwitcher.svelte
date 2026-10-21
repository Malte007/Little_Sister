<script lang="ts">
	import { i18n, languages, t } from '#lib/i18n/index.svelte.js';
</script>

<select
	aria-label={t('common.language')}
	value={i18n.language}
	onchange={(e) => i18n.set(e.currentTarget.value)}
>
	{#each languages as lang (lang)}
		<option value={lang}>{lang.toUpperCase()}</option>
	{/each}
</select>

<style>
	select {
		width: auto;
		padding: 0.35rem 0.6rem;
		font-size: 0.85rem;
	}
</style>
