<script lang="ts">
	import { t } from '#lib/i18n/index.svelte.js';
</script>

<span class="logo">
	<svg viewBox="0 0 32 32" aria-hidden="true">
		<circle cx="16" cy="16" r="10" fill="none" stroke="currentColor" stroke-width="2.5" />
		<circle cx="16" cy="16" r="4" fill="currentColor" />
	</svg>
	{t('app.name')}
</span>

<style>
	.logo {
		display: inline-flex;
		align-items: center;
		gap: 0.55rem;
		font-weight: 700;
		letter-spacing: -0.01em;
	}
	svg {
		width: 1.6rem;
		height: 1.6rem;
		color: var(--accent);
	}
</style>
