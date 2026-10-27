// auth-pages.js — đăng nhập, đăng ký (2 bước: thông tin → OTP), quên mật khẩu.

const AuthPages = {
  showMsg(box, text, kind = 'error') {
    box.className = kind === 'error' ? 'form-error' : 'form-note';
    box.textContent = text;
    box.classList.remove('hidden');
  },
  hideMsg(box) { box.classList.add('hidden'); },

  bindPasswordToggles() {
    $$('[data-toggle-pw]').forEach(btn => btn.addEventListener('click', () => {
      const input = btn.parentElement.querySelector('input');
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      btn.textContent = show ? 'Ẩn' : 'Hiện';
    }));
  },

  /** Ghi chú mã OTP khi server chạy chế độ dev (không gửi email thật). */
  devOtpNote(box, data) {
    if (data?.devOtp) AuthPages.showMsg(box, `Chế độ dev chưa gửi email thật. Mã OTP là ${data.devOtp} (cũng in trong console của server).`, 'note');
  },

  /** Đếm ngược nút "Gửi lại mã". */
  cooldown(btn, seconds) {
    const label = 'Gửi lại mã';
    let left = seconds;
    btn.disabled = true;
    const tick = () => {
      if (left <= 0) { btn.disabled = false; btn.textContent = label; clearInterval(t); return; }
      btn.textContent = `${label} (${left}s)`;
      left--;
    };
    const t = setInterval(tick, 1000);
    tick();
  },

  // ============================================================ đăng nhập

  login() {
    Auth.redirectIfLoggedIn();
    AuthPages.bindPasswordToggles();
    const form = $('#loginForm'), msg = $('#formMsg');
    const params = new URLSearchParams(location.search);
    if (params.get('expired')) AuthPages.showMsg(msg, 'Phiên đăng nhập đã hết hạn, hãy đăng nhập lại.', 'note');
    if (params.get('reset')) AuthPages.showMsg(msg, 'Đã đổi mật khẩu. Đăng nhập bằng mật khẩu mới.', 'note');
    if (params.get('return')) $('#toRegister').href = '/register.html?return=' + encodeURIComponent(params.get('return'));

    form.addEventListener('submit', e => {
      e.preventDefault();
      const login = form.login.value.trim(), password = form.password.value;
      if (!login || !password) return AuthPages.showMsg(msg, 'Nhập email/biệt danh và mật khẩu.');
      UI.busy(form.querySelector('[type=submit]'), async () => {
        try {
          Auth.save(await Api.post('/auth/login', { login, password }));
          location.replace(Auth.returnUrl());
        } catch (err) {
          if (err.code === 'AUTH_NOT_VERIFIED' && login.includes('@')) {
            location.href = '/register.html?verify=' + encodeURIComponent(login);
            return;
          }
          AuthPages.showMsg(msg, err.message);
        }
      });
    });
  },

  // ============================================================ đăng ký

  register() {
    Auth.redirectIfLoggedIn();
    AuthPages.bindPasswordToggles();
    const form = $('#registerForm'), msg = $('#formMsg'), otpMsg = $('#otpMsg');
    let email = '';

    const otp = UI.codeBoxes($('#otpBoxes'), 6, () => verify());

    const goOtp = (data) => {
      email = data.email;
      $('#otpEmail').textContent = email;
      $('#stepForm').classList.add('hidden');
      $('#stepOtp').classList.remove('hidden');
      AuthPages.hideMsg(otpMsg);
      AuthPages.devOtpNote(otpMsg, data);
      AuthPages.cooldown($('#resendBtn'), data.resendAfterSec ?? 60);
      otp.clear();
    };

    form.addEventListener('submit', e => {
      e.preventDefault();
      AuthPages.hideMsg(msg);
      const body = {
        email: form.email.value.trim(),
        nickname: form.nickname.value.trim(),
        password: form.password.value
      };
      if (!/^\S+@\S+\.\S+$/.test(body.email)) return AuthPages.showMsg(msg, 'Email không hợp lệ.');
      if (!/^[a-zA-Z0-9_]{3,20}$/.test(body.nickname)) return AuthPages.showMsg(msg, 'Biệt danh gồm 3–20 ký tự: chữ không dấu, số hoặc dấu gạch dưới.');
      if (body.password.length < 8 || !/[a-zA-Z]/.test(body.password) || !/\d/.test(body.password))
        return AuthPages.showMsg(msg, 'Mật khẩu cần ít nhất 8 ký tự, có cả chữ và số.');
      if (body.password !== form.confirm.value) return AuthPages.showMsg(msg, 'Hai mật khẩu chưa khớp.');

      UI.busy(form.querySelector('[type=submit]'), async () => {
        try { goOtp(await Api.post('/auth/register', body)); }
        catch (err) { AuthPages.showMsg(msg, err.message); }
      });
    });

    const verify = () => {
      const code = otp();
      if (code.length !== 6) return AuthPages.showMsg(otpMsg, 'Nhập đủ 6 chữ số.');
      UI.busy($('#verifyBtn'), async () => {
        try {
          Auth.save(await Api.post('/auth/verify-otp', { email, otp: code }));
          location.replace(Auth.returnUrl());
        } catch (err) {
          AuthPages.showMsg(otpMsg, err.message);
          otp.clear();
        }
      });
    };
    $('#verifyBtn').addEventListener('click', verify);

    $('#resendBtn').addEventListener('click', () => UI.busy($('#resendBtn'), async () => {
      try {
        const data = await Api.post('/auth/resend-otp', { email, purpose: 'REGISTER' });
        AuthPages.devOtpNote(otpMsg, data);
        if (!data.devOtp) AuthPages.showMsg(otpMsg, 'Đã gửi mã mới, kiểm tra hộp thư (cả mục Spam).', 'note');
        setTimeout(() => AuthPages.cooldown($('#resendBtn'), data.resendAfterSec ?? 60));
      } catch (err) { AuthPages.showMsg(otpMsg, err.message); }
    }));

    $('#backBtn').addEventListener('click', () => {
      $('#stepOtp').classList.add('hidden');
      $('#stepForm').classList.remove('hidden');
    });

    // Từ trang đăng nhập chuyển sang khi tài khoản chưa kích hoạt
    const verifyEmail = new URLSearchParams(location.search).get('verify');
    if (verifyEmail) {
      email = verifyEmail;
      $('#otpEmail').textContent = email;
      $('#stepForm').classList.add('hidden');
      $('#stepOtp').classList.remove('hidden');
      AuthPages.showMsg(otpMsg, 'Tài khoản chưa kích hoạt. Bấm "Gửi lại mã" để nhận mã mới.', 'note');
    }
  },

  // ============================================================ quên mật khẩu

  forgot() {
    Auth.redirectIfLoggedIn();
    AuthPages.bindPasswordToggles();
    const emailForm = $('#emailForm'), resetForm = $('#resetForm');
    const msg = $('#formMsg'), resetMsg = $('#resetMsg');
    let email = '';
    const otp = UI.codeBoxes($('#otpBoxes'), 6);

    emailForm.addEventListener('submit', e => {
      e.preventDefault();
      email = emailForm.email.value.trim();
      if (!/^\S+@\S+\.\S+$/.test(email)) return AuthPages.showMsg(msg, 'Email không hợp lệ.');
      UI.busy(emailForm.querySelector('[type=submit]'), async () => {
        try {
          const data = await Api.post('/auth/forgot-password', { email });
          email = data.email;
          $('#otpEmail').textContent = email;
          $('#stepEmail').classList.add('hidden');
          $('#stepReset').classList.remove('hidden');
          AuthPages.devOtpNote(resetMsg, data);
          AuthPages.cooldown($('#resendBtn'), data.resendAfterSec ?? 60);
          otp.focus();
        } catch (err) { AuthPages.showMsg(msg, err.message); }
      });
    });

    resetForm.addEventListener('submit', e => {
      e.preventDefault();
      const code = otp(), password = resetForm.password.value;
      if (code.length !== 6) return AuthPages.showMsg(resetMsg, 'Nhập đủ 6 chữ số của mã.');
      if (password.length < 8 || !/[a-zA-Z]/.test(password) || !/\d/.test(password))
        return AuthPages.showMsg(resetMsg, 'Mật khẩu cần ít nhất 8 ký tự, có cả chữ và số.');
      if (password !== resetForm.confirm.value) return AuthPages.showMsg(resetMsg, 'Hai mật khẩu chưa khớp.');
      UI.busy(resetForm.querySelector('[type=submit]'), async () => {
        try {
          await Api.post('/auth/reset-password', { email, otp: code, newPassword: password });
          location.replace('/login.html?reset=1');
        } catch (err) { AuthPages.showMsg(resetMsg, err.message); }
      });
    });

    $('#resendBtn').addEventListener('click', () => UI.busy($('#resendBtn'), async () => {
      try {
        const data = await Api.post('/auth/resend-otp', { email, purpose: 'RESET_PASSWORD' });
        AuthPages.devOtpNote(resetMsg, data);
        setTimeout(() => AuthPages.cooldown($('#resendBtn'), data.resendAfterSec ?? 60));
      } catch (err) { AuthPages.showMsg(resetMsg, err.message); }
    }));
  }
};
