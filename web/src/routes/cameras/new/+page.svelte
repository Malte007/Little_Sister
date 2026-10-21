<script lang="ts">
	// Kamera hinzufügen: per ONVIF-Suche (empfohlen) oder manuell mit RTSP-Adresse
	import { onMount } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	import { goto } from '$app/navigation';
	import {
		api,
		ApiError,
		type CameraInput,
		type DiscoveredDevice,
		type OnvifProbeResult
	} from '#lib/api.js';
	import CameraForm from '#lib/components/CameraForm.svelte';
	import Field from '#lib/components/Field.svelte';
	import { t, tError } from '#lib/i18n/index.svelte.js';

	type Step = 'search' | 'connect' | 'form';
	let step = $state<Step>('search');

	// Suche
	let searching = $state(false);
	let devices = $state<DiscoveredDevice[]>([]);
	let searchError = $state<string>();

	// Verbinden per ONVIF
	let address = $state('');
	let deviceName = $state('');
	let username = $state('admin');
	let password = $state('');
	let connecting = $state(false);
	let connectError = $state<string>();
	let probe = $state<OnvifProbeResult>();

	let initial = $state<CameraInput>(empty());

	function empty(): CameraInput {
		return {
			name: '',
			mainStreamUrl: '',
			subStreamUrl: null,
			username: null,
			password: null,
			onvifAddress: null,
			manufacturer: null,
			model: null,
			recordingMode: 'Continuous',
			preRollSeconds: 10,
			postRollSeconds: 30,
			quotaGb: 200,
			maxRetentionDays: null,
			rotation: 0
		};
	}

	async function search() {
		searching = true;
		searchError = undefined;
		try {
			devices = await api<DiscoveredDevice[]>('/api/onvif/discover', { method: 'POST' });
		} catch (e) {
			searchError = e instanceof ApiError ? tError(e.code) : t('errors.network');
		} finally {
			searching = false;
		}
	}

	onMount(search);

	function choose(device?: DiscoveredDevice) {
		address = device?.address ?? address;
		deviceName = device?.name ?? device?.hardware ?? device?.host ?? address;
		connectError = undefined;
		probe = undefined;
		step = 'connect';
	}

	async function connect(event: SubmitEvent) {
		event.preventDefault();
		connecting = true;
		connectError = undefined;
		try {
			probe = await api<OnvifProbeResult>('/api/onvif/probe', {
				method: 'POST',
				body: { address, username, password }
			});
			const model = [probe.device.manufacturer, probe.device.model].filter(Boolean).join(' ');
			initial = {
				...empty(),
				name: deviceName && deviceName !== address ? deviceName : (probe.device.model ?? ''),
				mainStreamUrl: probe.suggestedMainStream ?? '',
				subStreamUrl: probe.suggestedSubStream,
				username,
				password,
				onvifAddress: address,
				manufacturer: probe.device.manufacturer,
				model: probe.device.model
			};
			deviceName = model || deviceName;
			step = 'form';
		} catch (e) {
			connectError = e instanceof ApiError ? tError(e.code) : t('errors.network');
		} finally {
			connecting = false;
		}
	}

	function manual() {
		probe = undefined;
		initial = empty();
		step = 'form';
	}

	async function save(input: CameraInput) {
		await api('/api/cameras', { method: 'POST', body: input });
		await goto('/cameras');
	}
</script>

<div class="page">
	<h1>{t('cameras.newTitle')}</h1>
	<p class="muted">{t('cameras.enableHint')}</p>

	{#key step}
		<div in:fly={{ y: 10, duration: 300 }}>
			{#if step === 'search'}
				<section class="card">
					<div class="bar">
						<h2>{t('cameras.search')}</h2>
						<button class="secondary" onclick={search} disabled={searching}>
							{t('cameras.searchAgain')}
						</button>
					</div>

					{#if searching}
						<p class="muted searching"><span class="radar"></span>{t('cameras.searching')}</p>
					{:else if searchError}
						<div class="alert">{searchError}</div>
					{:else if devices.length === 0}
						<p><strong>{t('cameras.noneFound')}</strong></p>
						<p class="muted small">{t('cameras.noneFoundHint')}</p>
					{:else}
						<ul class="devices">
							{#each devices as device (device.address)}
								<li in:fade>
									<div>
										<strong>{device.name ?? device.hardware ?? device.host}</strong>
										<div class="muted small">
											{[device.hardware, device.host].filter(Boolean).join(' · ')}
										</div>
									</div>
									<button onclick={() => choose(device)}>{t('cameras.select')}</button>
								</li>
							{/each}
						</ul>
					{/if}

					<form
						class="direct"
						onsubmit={(e) => {
							e.preventDefault();
							choose();
						}}
					>
						<Field
							label={t('cameras.address')}
							bind:value={address}
							hint={t('cameras.addressHint')}
							spellcheck="false"
						/>
						<button type="submit" class="secondary" disabled={!address}>{t('common.next')}</button>
					</form>
				</section>

				<button class="link" onclick={manual}>{t('cameras.manual')} →</button>
			{:else if step === 'connect'}
				<section class="card narrow">
					<h2>{t('cameras.connectTitle', { name: deviceName })}</h2>
					{#if connectError}<div class="alert" role="alert">{connectError}</div>{/if}
					<form onsubmit={connect}>
						<Field
							label={t('cameras.username')}
							bind:value={username}
							autocomplete="off"
							spellcheck="false"
						/>
						<Field
							label={t('cameras.password')}
							type="password"
							bind:value={password}
							autocomplete="new-password"
						/>
						<div class="actions">
							<button type="button" class="secondary" onclick={() => (step = 'search')}>
								{t('common.back')}
							</button>
							<button type="submit" disabled={connecting}>
								{connecting ? t('cameras.connecting') : t('cameras.connect')}
							</button>
						</div>
					</form>
				</section>
			{:else}
				<section class="card narrow">
					{#if probe}
						<p class="found">{t('cameras.found', { device: deviceName })}</p>
						<details>
							<summary class="muted small">{t('cameras.profiles')}</summary>
							<ul class="profiles small">
								{#each probe.profiles as profile (profile.token)}
									<li>
										<strong>{profile.name}</strong> · {profile.encoding}
										{profile.width} × {profile.height}
										<code>{profile.streamUri}</code>
									</li>
								{/each}
							</ul>
						</details>
					{/if}
					<CameraForm {initial} onsave={save} />
					<button class="link" onclick={() => (step = 'search')}>← {t('cameras.back')}</button>
				</section>
			{/if}
		</div>
	{/key}
</div>

<style>
	.page {
		max-width: 44rem;
	}
	h2 {
		font-size: 1.1rem;
		margin: 0;
	}
	.narrow h2 {
		margin-bottom: 1rem;
	}
	.card {
		padding: 1.5rem;
		margin-top: 1rem;
	}
	.bar {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 1rem;
	}
	.bar button {
		padding: 0.45rem 0.9rem;
	}
	.searching {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}
	.radar {
		width: 1rem;
		height: 1rem;
		border-radius: 50%;
		border: 2px solid var(--accent);
		animation: ping 1.2s var(--ease) infinite;
	}
	@keyframes ping {
		from {
			box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 50%, transparent);
		}
		to {
			box-shadow: 0 0 0 12px transparent;
		}
	}
	.devices {
		list-style: none;
		padding: 0;
		margin: 0 0 1rem;
	}
	.devices li {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 1rem;
		padding: 0.8rem 0;
		border-bottom: 1px solid var(--border);
	}
	.devices button {
		padding: 0.45rem 0.9rem;
	}
	.direct {
		display: flex;
		gap: 0.75rem;
		align-items: flex-start;
		margin-top: 1.25rem;
	}
	.direct :global(.field) {
		flex: 1;
	}
	.direct button {
		margin-top: 1.6rem;
	}
	.small {
		font-size: 0.8rem;
	}
	.link {
		background: none;
		color: var(--accent);
		padding: 0.75rem 0;
		margin-top: 0.5rem;
	}
	.link:hover:not(:disabled) {
		background: none;
		text-decoration: underline;
	}
	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
	}
	.found {
		color: var(--success);
		font-weight: 600;
	}
	details {
		margin-bottom: 1.25rem;
	}
	.profiles {
		padding-left: 1.1rem;
	}
	.profiles code {
		display: block;
		word-break: break-all;
		color: var(--text-muted);
	}
</style>
