// auth.js — lưu JWT và thông tin người dùng trong localStorage, chặn trang khi chưa đăng nhập.

const Auth = {
  TOKEN_KEY: 'ct.token',
  USER_KEY: 'ct.user',
  EXP_KEY: 'ct.expiresAt',

  get token() {
    const token = localStorage.getItem(this.TOKEN_KEY);
    const exp = localStorage.getItem(this.EXP_KEY);
    if (token && exp && new Date(exp) <= new Date()) {
      this.clear();
      return null;
    }
    return token;
  },

  get user() {
    try { return JSON.parse(localStorage.getItem(this.USER_KEY)); } catch { return null; }
  },

  get isLoggedIn() { return !!this.token; },

  /** Lưu kết quả của /auth/login hoặc /auth/verify-otp. */
  save(authResponse) {
    localStorage.setItem(this.TOKEN_KEY, authResponse.accessToken);
    localStorage.setItem(this.EXP_KEY, authResponse.expiresAt);
    localStorage.setItem(this.USER_KEY, JSON.stringify(authResponse.user));
  },

  updateUser(user) { localStorage.setItem(this.USER_KEY, JSON.stringify(user)); },

  clear() {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    localStorage.removeItem(this.EXP_KEY);
  },

  /** Gọi đầu mỗi trang cần đăng nhập. Chưa đăng nhập → chuyển sang login.html?return=... */
  requireLogin() {
    if (this.isLoggedIn) return true;
    const ret = encodeURIComponent(location.pathname + location.search);
    location.replace('/login.html?return=' + ret);
    return false;
  },

  /** Trang đăng nhập/đăng ký: đã đăng nhập thì chuyển thẳng vào trong. */
  redirectIfLoggedIn() {
    if (this.isLoggedIn) location.replace(this.returnUrl());
  },

  returnUrl() {
    const ret = new URLSearchParams(location.search).get('return');
    // Chỉ cho phép đường dẫn nội bộ để tránh open redirect
    return ret && ret.startsWith('/') && !ret.startsWith('//') ? ret : '/lobby.html';
  },

  logout(expired = false) {
    this.clear();
    const ret = encodeURIComponent(location.pathname + location.search);
    location.replace('/login.html' + (expired ? '?expired=1&return=' + ret : ''));
  }
};
