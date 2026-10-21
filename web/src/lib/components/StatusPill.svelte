<script lang="ts">
	// Farbiger Zustand einer Kamera; "Nimmt auf" pulsiert dezent, Bewegung erscheint als eigene Marke
	import type { CameraState } from '#lib/api.js';
	import { t } from '#lib/i18n/index.svelte.js';

	let {
		state,
		title,
		motion = false
	}: { state: CameraState; title?: string; motion?: boolean } = $props();
</script>

<span class="pills">
	<span class="pill {state.toLowerCase()}" {title}>
		<span class="dot"></span>
		{t(`status.${state}`)}
	</span>
	{#if motion}
		<span class="pill motion">
			<span class="dot"></span>
			{t('status.motion')}
		</span>
	{/if}
</span>

<style>
	.pills {
		display: inline-flex;
		gap: 0.35rem;
		flex-wrap: wrap;
	}
	.pill {
		display: inline-flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.8rem;
		font-weight: 600;
		padding: 0.2rem 0.65rem;
		border-radius: 999px;
		background: var(--bg-input);
		color: var(--text-muted);
		white-space: nowrap;
	}
	.dot {
		width: 0.5rem;
		height: 0.5rem;
		border-radius: 50%;
		background: currentColor;
	}
	.recording {
		color: var(--success);
		background: color-mix(in srgb, var(--success) 14%, transparent);
	}
	.recording .dot {
		animation: blink 1.6s ease-in-out infinite;
	}
	.connecting {
		color: var(--accent);
		background: color-mix(in srgb, var(--accent) 14%, transparent);
	}
	.standby {
		color: var(--accent);
	}
	.motion {
		color: var(--motion);
		background: color-mix(in srgb, var(--motion) 16%, transparent);
	}
	.motion .dot {
		animation: blink 0.8s ease-in-out infinite;
	}
	.error {
		color: var(--danger);
		background: color-mix(in srgb, var(--danger) 14%, transparent);
	}
	@keyframes blink {
		50% {
			opacity: 0.25;
		}
	}
</style>
