let csrf;
export async function api(path, { method = 'GET', body } = {}) {
    const headers = {};
    if (method !== 'GET') {
        csrf ??= (await (await fetch('/api/auth/csrf', { credentials: 'same-origin' })).json()).token;
        headers['X-CSRF-TOKEN'] = csrf;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        const error = new Error(problem.title || (response.status === 429 ? 'Quá nhiều yêu cầu. Vui lòng thử lại sau một phút.' : 'Không thể kết nối. Vui lòng thử lại.'));
        error.status = response.status;
        throw error;
    }
    return response.status === 204 ? null : response.json();
}
export function resetCsrf() { csrf = undefined; }
