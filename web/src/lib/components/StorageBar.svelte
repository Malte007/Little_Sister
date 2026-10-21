<script lang="ts">
	// Belegung als Balken mit Text darunter
	let { value, max, label }: { value: number; max: number; label: string } = $props();
	const percent = $derived(max > 0 ? Math.min(100, (value / max) * 100) : 0);
</script>

<div
	class="bar"
	role="meter"
	aria-valuenow={Math.round(percent)}
	aria-valuemin="0"
	aria-valuemax="100"
	aria-label={label}
>
	<span style:width="{percent}%" class:full={percent > 90}></span>
</div>
<p class="muted label">{label}</p>

<style>
	.bar {
		height: 6px;
		border-radius: 999px;
		background: var(--bg-input);
		overflow: hidden;
	}
	.bar span {
		display: block;
		height: 100%;
		background: var(--accent);
		border-radius: inherit;
		transition: width 0.6s var(--ease);
	}
	.bar span.full {
		background: var(--danger);
	}
	.label {
		font-size: 0.8rem;
		margin: 0.4rem 0 0;
	}
</style>
