import { describe, expect, it } from 'vitest';
import { ApiError, toApiError } from './api';

describe('toApiError', () => {
	it('übernimmt Code und Feldfehler aus ProblemDetails', async () => {
		const response = new Response(
			JSON.stringify({
				title: 'One or more validation errors occurred.',
				errors: { Password: ['PasswordTooShort'] }
			}),
			{ status: 400 }
		);
		const error = await toApiError(response);
		expect(error.status).toBe(400);
		expect(error.field('password')).toBe('PasswordTooShort');
	});

	it('kommt ohne JSON-Körper aus', async () => {
		const error = await toApiError(new Response('', { status: 429 }));
		expect(error).toBeInstanceOf(ApiError);
		expect(error.code).toBe('http429');
	});
});
