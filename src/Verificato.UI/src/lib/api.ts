let csrf = '';
export class ApiError extends Error {
  constructor(message: string, public readonly status: number) { super(message); }
}
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api/v1${path}`, {
    ...init, credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-Verificato-CSRF': csrf, ...init.headers },
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string };
    throw new ApiError(problem.title || `Błąd żądania (${response.status})`, response.status);
  }
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T;
  const text = await response.text();
  return text ? JSON.parse(text) as T : undefined as T;
}
export async function authenticate(): Promise<void> {
  const fragment = new URLSearchParams(location.hash.slice(1));
  const token = fragment.get('bootstrap');
  if (token) {
    history.replaceState(null, '', location.pathname + location.search);
    const result = await api<{ csrf: string }>('/auth/bootstrap', { method: 'POST', body: JSON.stringify({ token }) });
    csrf = result.csrf;
  } else {
    const result = await api<{ csrf: string }>('/auth/session');
    csrf = result.csrf;
  }
}
export const post = <T,>(path: string, body?: unknown, idempotent = false) => api<T>(path, {
  method: 'POST', body: body === undefined ? undefined : JSON.stringify(body),
  headers: idempotent ? { 'Idempotency-Key': crypto.randomUUID() } : {},
});
export function message(error: unknown): string { return error instanceof Error ? error.message : 'Wystąpił nieoczekiwany błąd.'; }
