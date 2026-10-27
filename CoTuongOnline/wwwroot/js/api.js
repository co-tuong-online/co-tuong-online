// api.js — gọi REST API /api/v1. Mọi response có dạng { success, code, message, data }.
// Dùng: const room = await Api.post('/rooms', { config }); lỗi sẽ ném ApiError { code, message, status }.

class ApiError extends Error {
  constructor(code, message, status) {
    super(message);
    this.code = code;
    this.status = status;
  }
}

const Api = {
  base: '/api/v1',

  async request(method, path, body) {
    const headers = { 'Accept': 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const token = Auth.token;
    if (token) headers['Authorization'] = 'Bearer ' + token;

    let res;
    try {
      res = await fetch(this.base + path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
      });
    } catch {
      throw new ApiError('NETWORK_ERROR', 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.', 0);
    }

    let json = null;
    try { json = await res.json(); } catch { /* response rỗng */ }

    if (res.status === 401 && token) {
      // Token hết hạn hoặc không hợp lệ → về trang đăng nhập
      Auth.logout(true);
      throw new ApiError('UNAUTHORIZED', 'Phiên đăng nhập đã hết hạn.', 401);
    }
    if (!res.ok || !json || json.success === false) {
      throw new ApiError(json?.code || 'HTTP_' + res.status, json?.message || 'Có lỗi xảy ra, vui lòng thử lại.', res.status);
    }
    return json.data;
  },

  get(path) { return this.request('GET', path); },
  post(path, body = {}) { return this.request('POST', path, body); },
  put(path, body = {}) { return this.request('PUT', path, body); },
  patch(path, body = {}) { return this.request('PATCH', path, body); },
  del(path) { return this.request('DELETE', path); }
};
