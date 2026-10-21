<script lang="ts">
	// Nur sichtbar, solange es noch keine Kamera gibt
	import { t } from '#lib/i18n/index.svelte.js';
</script>

<h1>{t('playback.title')}</h1>
<p class="muted">{t('playback.noCameras')}</p>
