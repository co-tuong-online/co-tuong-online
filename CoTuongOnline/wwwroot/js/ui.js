// ui.js — tiện ích giao diện dùng chung: toast, hộp thoại, thanh trên, định dạng, âm thanh.

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

const UI = {
  escape(text) {
    return String(text ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  },

  /** Tạo phần tử từ chuỗi HTML. */
  el(html) {
    const t = document.createElement('template');
    t.innerHTML = html.trim();
    return t.content.firstElementChild;
  },

  // ------------------------------------------------------------ toast

  toast(message, type = 'info', ms = 3800) {
    let box = $('.toasts');
    if (!box) {
      box = UI.el('<div class="toasts" role="status" aria-live="polite"></div>');
      document.body.appendChild(box);
    }
    const t = UI.el(`<div class="toast ${type}">${UI.escape(message)}</div>`);
    box.appendChild(t);
    setTimeout(() => { t.style.opacity = '0'; t.style.transition = 'opacity .3s'; }, ms);
    setTimeout(() => t.remove(), ms + 320);
  },

  error(err) { UI.toast(err?.message || String(err), 'error'); },

  // ------------------------------------------------------------ hộp thoại

  /**
   * Mở hộp thoại. buttons: [{ label, value, kind: 'primary'|'danger'|'ghost'|'' }]
   * Trả về Promise với value của nút được bấm (Esc/bấm ra ngoài → null nếu dismissible).
   */
  modal({ title = '', html = '', buttons = [{ label: 'Đóng', value: null }], dismissible = true, center = false, onOpen } = {}) {
    return new Promise(resolve => {
      const backdrop = UI.el(`
        <div class="modal-backdrop">
          <div class="modal ${center ? 'center' : ''}" role="dialog" aria-modal="true" aria-label="${UI.escape(title)}">
            ${title ? `<h2>${UI.escape(title)}</h2>` : ''}
            <div class="modal-body">${html}</div>
            <div class="actions"></div>
          </div>
        </div>`);
      const actions = $('.actions', backdrop);
      const close = value => {
        document.removeEventListener('keydown', onKey);
        backdrop.remove();
        resolve(value);
      };
      buttons.forEach(b => {
        const btn = UI.el(`<button type="button" class="btn ${b.kind || ''}">${UI.escape(b.label)}</button>`);
        btn.addEventListener('click', () => {
          if (b.validate && b.validate(backdrop) === false) return;
          close(typeof b.value === 'function' ? b.value(backdrop) : b.value);
        });
        actions.appendChild(btn);
      });
      const onKey = e => { if (e.key === 'Escape' && dismissible) close(null); };
      document.addEventListener('keydown', onKey);
      if (dismissible) backdrop.addEventListener('mousedown', e => { if (e.target === backdrop) close(null); });
      document.body.appendChild(backdrop);
      backdrop.close = close;
      onOpen?.(backdrop, close);
      const focusTarget = $('input, select, textarea', backdrop) || $('.actions .btn:last-child', backdrop);
      focusTarget?.focus();
    });
  },

  async confirm(title, message, okLabel = 'Đồng ý', danger = false) {
    const v = await UI.modal({
      title,
      html: `<p class="muted">${UI.escape(message)}</p>`,
      buttons: [
        { label: 'Huỷ', value: false, kind: 'ghost' },
        { label: okLabel, value: true, kind: danger ? 'danger' : 'primary' }
      ]
    });
    return v === true;
  },

  async prompt(title, label, value = '', okLabel = 'Lưu', maxLength = 50) {
    return UI.modal({
      title,
      html: `<label class="field"><span>${UI.escape(label)}</span>
               <input class="input" id="promptInput" maxlength="${maxLength}" value="${UI.escape(value)}"></label>`,
      buttons: [
        { label: 'Huỷ', value: null, kind: 'ghost' },
        {
          label: okLabel, kind: 'primary',
          validate: m => $('#promptInput', m).value.trim().length > 0,
          value: m => $('#promptInput', m).value.trim()
        }
      ],
      onOpen: (m, close) => {
        const input = $('#promptInput', m);
        input.select();
        input.addEventListener('keydown', e => { if (e.key === 'Enter' && input.value.trim()) close(input.value.trim()); });
      }
    });
  },

  // ------------------------------------------------------------ nút đang xử lý

  async busy(button, fn) {
    if (button.disabled) return;
    const html = button.innerHTML;
    button.disabled = true;
    button.innerHTML = `<span class="spinner"></span>${html}`;
    try { return await fn(); }
    finally {
      button.disabled = false;
      button.innerHTML = html;
    }
  },

  // ------------------------------------------------------------ thanh trên

  renderTopbar(active) {
    const user = Auth.user;
    const bar = UI.el(`
      <header class="topbar">
        <a class="brand" href="/lobby.html"><span class="brand-mark" aria-hidden="true">帥</span><span class="brand-name">Cờ Tướng Online</span></a>
        <nav class="nav">
          <a href="/lobby.html" class="${active === 'lobby' ? 'active' : ''}">Sảnh</a>
          <a href="/ai-game.html" class="${active === 'ai' ? 'active' : ''}">Chơi với máy</a>
          <a href="/saved-games.html" class="${active === 'saved' ? 'active' : ''}">Ván đã lưu</a>
        </nav>
        <div class="spacer"></div>
        <div class="user-chip">
          <span class="conn-dot" id="connDot" title="Kết nối thời gian thực"></span>
          <span><b>${UI.escape(user?.nickname ?? '')}</b> <span class="stats" id="userStats"></span></span>
          <button class="btn ghost small" id="logoutBtn">Đăng xuất</button>
        </div>
      </header>`);
    document.body.prepend(bar);
    $('#logoutBtn', bar).addEventListener('click', async () => {
      if (await UI.confirm('Đăng xuất', 'Bạn muốn đăng xuất khỏi tài khoản này?', 'Đăng xuất')) Auth.logout();
    });
    UI.renderStats(user);
  },

  renderStats(user) {
    const s = $('#userStats');
    if (s && user) s.textContent = `· ${user.wins} thắng, ${user.losses} thua`;
  },

  setConnState(state) {
    const dot = $('#connDot');
    if (!dot) return;
    dot.className = 'conn-dot ' + (state === 'on' ? 'on' : state === 'wait' ? 'wait' : '');
    dot.title = state === 'on' ? 'Đã kết nối' : state === 'wait' ? 'Đang kết nối lại…' : 'Mất kết nối';
  },

  // ------------------------------------------------------------ định dạng

  /** mm:ss từ mili giây. */
  clock(ms) {
    const total = Math.max(0, Math.ceil(ms / 1000));
    const m = Math.floor(total / 60), s = total % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  },

  /** Giờ Việt Nam dd/MM/yyyy HH:mm. */
  dateTime(iso) {
    const d = new Date(iso);
    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', hour12: false
    }).formatToParts(d).reduce((a, p) => (a[p.type] = p.value, a), {});
    return `${parts.day}/${parts.month}/${parts.year} ${parts.hour}:${parts.minute}`;
  },

  timeConfig(cfg) { return `${cfg.totalTimeMin} phút, ${cfg.moveTimeSec} giây/nước`; },

  colorName(color) { return color === 'red' ? 'Đỏ' : 'Đen'; },

  endReason(reason) {
    return {
      CHECKMATE: 'Chiếu bí',
      STALEMATE: 'Hết nước đi',
      RESIGN: 'Đầu hàng',
      TIMEOUT: 'Hết thời gian',
      DISCONNECT: 'Mất kết nối quá lâu',
      NO_CAPTURE_LIMIT: '60 nước liền không ăn quân',
      REPETITION: 'Lặp lại cùng một thế cờ 3 lần'
    }[reason] || reason || '';
  },

  /** Kết quả theo góc nhìn người chơi: 'win' | 'lose' | 'draw'. */
  outcomeFor(result, myColor) {
    if (result === 'DRAW') return 'draw';
    const winner = result === 'RED_WIN' ? 'red' : 'black';
    return winner === myColor ? 'win' : 'lose';
  },

  /** Hộp thoại kết thúc ván. */
  async showResult({ result, reason, myColor, extraButtons = [] }) {
    const outcome = UI.outcomeFor(result, myColor);
    const title = { win: 'Bạn thắng!', lose: 'Bạn thua', draw: 'Hoà cờ' }[outcome];
    const glyph = { win: '勝', lose: '負', draw: '和' }[outcome];
    if (outcome === 'win') Sound.play('win');
    return UI.modal({
      center: true,
      html: `<div class="result-glyph ${outcome}" aria-hidden="true">${glyph}</div>
             <h2>${title}</h2>
             <p class="muted">${UI.escape(UI.endReason(reason))}</p>`,
      buttons: extraButtons.length ? extraButtons : [{ label: 'Đóng', value: null, kind: 'primary' }]
    });
  },

  /** Ghép các ô nhập 1 chữ số (OTP, mã phòng). Trả về hàm lấy giá trị. */
  codeBoxes(container, length, onComplete) {
    container.innerHTML = '';
    container.classList.add('code-boxes');
    const inputs = [];
    for (let i = 0; i < length; i++) {
      const inp = UI.el(`<input inputmode="numeric" maxlength="1" autocomplete="one-time-code" aria-label="Chữ số ${i + 1}">`);
      inp.addEventListener('input', () => {
        inp.value = inp.value.replace(/\D/g, '').slice(-1);
        if (inp.value && i < length - 1) inputs[i + 1].focus();
        if (value().length === length) onComplete?.(value());
      });
      inp.addEventListener('keydown', e => {
        if (e.key === 'Backspace' && !inp.value && i > 0) inputs[i - 1].focus();
        if (e.key === 'ArrowLeft' && i > 0) inputs[i - 1].focus();
        if (e.key === 'ArrowRight' && i < length - 1) inputs[i + 1].focus();
      });
      inp.addEventListener('paste', e => {
        const digits = (e.clipboardData.getData('text') || '').replace(/\D/g, '').slice(0, length);
        if (!digits) return;
        e.preventDefault();
        digits.split('').forEach((d, k) => { if (inputs[k]) inputs[k].value = d; });
        inputs[Math.min(digits.length, length - 1)].focus();
        if (digits.length === length) onComplete?.(value());
      });
      inputs.push(inp);
      container.appendChild(inp);
    }
    const value = () => inputs.map(x => x.value).join('');
    value.set = str => { str.split('').forEach((d, k) => { if (inputs[k]) inputs[k].value = d; }); };
    value.clear = () => { inputs.forEach(x => x.value = ''); inputs[0].focus(); };
    value.focus = () => inputs[0].focus();
    return value;
  }
};

// ------------------------------------------------------------ âm thanh (tổng hợp bằng WebAudio, không cần file)

const Sound = {
  ctx: null,
  get enabled() { return localStorage.getItem('ct.sound') !== 'off'; },
  set enabled(v) { localStorage.setItem('ct.sound', v ? 'on' : 'off'); },

  play(kind) {
    if (!this.enabled) return;
    try {
      this.ctx ??= new (window.AudioContext || window.webkitAudioContext)();
      const ctx = this.ctx, t = ctx.currentTime;
      const tone = (freq, start, dur, gain, type = 'triangle') => {
        const o = ctx.createOscillator(), g = ctx.createGain();
        o.type = type; o.frequency.setValueAtTime(freq, t + start);
        g.gain.setValueAtTime(gain, t + start);
        g.gain.exponentialRampToValueAtTime(0.0001, t + start + dur);
        o.connect(g).connect(ctx.destination);
        o.start(t + start); o.stop(t + start + dur + 0.02);
      };
      if (kind === 'move') { tone(420, 0, 0.07, 0.25); tone(190, 0, 0.12, 0.2, 'sine'); }
      else if (kind === 'capture') { tone(520, 0, 0.06, 0.3); tone(240, 0.03, 0.16, 0.28, 'square'); }
      else if (kind === 'check') { tone(660, 0, 0.12, 0.18); tone(880, 0.12, 0.18, 0.18); }
      else if (kind === 'win') { [523, 659, 784, 1046].forEach((f, i) => tone(f, i * 0.11, 0.22, 0.15)); }
      else if (kind === 'notify') { tone(740, 0, 0.1, 0.15, 'sine'); tone(988, 0.1, 0.14, 0.15, 'sine'); }
    } catch { /* trình duyệt chặn âm thanh khi chưa tương tác */ }
  }
};
