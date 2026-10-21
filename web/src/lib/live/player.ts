// Live-Player auf Basis von go2rtcs VideoRTC: WebRTC (geringe Latenz) mit MSE als Rückfall.
// Die Verbindung läuft immer über die angemeldete Little-Sister-API, nie direkt zu go2rtc.
import { VideoRTC } from './video-rtc.js';

export type PlayerState = 'connecting' | 'playing' | 'error';

class LiveVideoElement extends VideoRTC {
	// STUN-Server bleiben wie bei go2rtc voreingestellt: Browser mit strenger WebRTC-Richtlinie
	// (z. B. Brave) liefern ohne sie keine eigenen Adressen, und WebRTC kommt nicht zustande.

	oninit() {
		super.oninit();
		this.video.controls = false;
		this.video.muted = true;
		this.video.style.objectFit = 'contain';
		this.video.addEventListener('playing', () => this.report('playing'));
		this.video.addEventListener('waiting', () => this.report('connecting'));
	}

	onopen() {
		const modes = super.onopen();
		// go2rtc meldet Fehler (z. B. Kamera nicht erreichbar) als Nachricht vom Typ "error"
		this.onmessage.littlesister = (msg: { type: string; value: string }) => {
			if (msg.type === 'error') this.report('error', msg.value);
		};
		return modes;
	}

	onclose() {
		const reconnecting = super.onclose();
		if (reconnecting) this.report('connecting');
		return reconnecting;
	}

	private report(state: PlayerState, detail?: string) {
		// Fehler eines Verfahrens ignorieren, solange das andere (WebRTC oder MSE) schon Bild liefert
		if (state === 'error' && !this.video.paused && this.video.readyState >= 2) return;
		this.dispatchEvent(new CustomEvent('playerstate', { detail: { state, detail } }));
	}
}

/** Registriert das Element einmalig als &lt;ls-live-video&gt;. */
export function definePlayer() {
	if (!customElements.get('ls-live-video'))
		customElements.define('ls-live-video', LiveVideoElement);
}

export type LivePlayer = LiveVideoElement;
