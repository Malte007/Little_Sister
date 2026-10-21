import { defineConfig } from 'vitest/config';
import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';

// Backend während der Entwicklung (dotnet run, Profil "http")
const backend = process.env.LS_BACKEND_URL ?? 'http://localhost:5080';

export default defineConfig({
	plugins: [
		sveltekit({
			compilerOptions: {
				// Runes-Modus für eigenen Code erzwingen
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			// Single-Page-App: alles wird statisch gebaut und vom .NET-Backend ausgeliefert
			adapter: adapter({ fallback: 'index.html' })
		})
	],
	server: {
		proxy: {
			'/api': backend,
			'/healthz': backend
		}
	},
	test: {
		expect: { requireAssertions: true },
		projects: [
			{
				extends: './vite.config.ts',
				test: {
					name: 'unit',
					environment: 'node',
					include: ['src/**/*.{test,spec}.{js,ts}'],
					exclude: ['src/**/*.svelte.{test,spec}.{js,ts}']
				}
			}
		]
	}
});
