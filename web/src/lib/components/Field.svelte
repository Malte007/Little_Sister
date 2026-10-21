<script lang="ts">
	// Eingabefeld mit Beschriftung, Hinweis und Fehlermeldung
	import type { HTMLInputAttributes } from 'svelte/elements';
	import { slide } from 'svelte/transition';

	let {
		label,
		value = $bindable(''),
		hint,
		error,
		id = crypto.randomUUID(),
		...rest
	}: HTMLInputAttributes & {
		label: string;
		value?: string;
		hint?: string;
		error?: string;
	} = $props();
</script>

<div class="field">
	<label for={id}>{label}</label>
	<input {id} bind:value aria-invalid={error ? 'true' : undefined} {...rest} />
	{#if error}
		<p class="error" transition:slide={{ duration: 180 }}>{error}</p>
	{:else if hint}
		<p class="hint muted">{hint}</p>
	{/if}
</div>

<style>
	.field {
		margin-bottom: 1.1rem;
	}
	.hint {
		font-size: 0.8rem;
		margin: 0.35rem 0 0;
	}
	.error {
		margin: 0.35rem 0 0;
	}
</style>
