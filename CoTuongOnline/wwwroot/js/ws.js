// ws.js — kết nối SignalR tới /hubs/game (dùng thư viện @microsoft/signalr trong /lib).
// - Realtime.on('MOVE_ACCEPTED', fn)       nhận sự kiện server phát
// - await Realtime.invoke('SendChat', code, text)   gọi phương thức Hub, lỗi nghiệp vụ ném ApiError
// - Realtime.onReconnected(fn)              chạy lại sau khi tự kết nối lại (phải gọi lại JoinRoom)
// Lời mời vào phòng (INVITE_RECEIVED) được xử lý chung ở đây cho mọi trang.

const Realtime = {
  connection: null,
  _reconnectHandlers: [],
  _startPromise: null,

  connect() {
    if (this._startPromise) return this._startPromise;
    if (this.connection) return (this._startPromise = this._startWithRetry());

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => Auth.token })
      .withAutomaticReconnect({
        // 0s, 2s, 5s, 10s rồi cứ 10s thử lại — không bỏ cuộc
        nextRetryDelayInMilliseconds: ctx => [0, 2000, 5000, 10000][ctx.previousRetryCount] ?? 10000
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // Khớp với KeepAlive 5s / ClientTimeout 15s ở server
    this.connection.serverTimeoutInMilliseconds = 15000;
    this.connection.keepAliveIntervalInMilliseconds = 5000;

    this.connection.onreconnecting(() => UI.setConnState('wait'));
    this.connection.onreconnected(async () => {
      UI.setConnState('on');
      for (const fn of this._reconnectHandlers) {
        try { await fn(); } catch (e) { console.error(e); }
      }
    });
    this.connection.onclose(() => {
      UI.setConnState('off');
      // Hết lượt tự kết nối lại → khởi động lại chính connection này (giữ nguyên các handler đã đăng ký)
      setTimeout(() => {
        this._startPromise = this._startWithRetry();
        this._startPromise.then(() => this._reconnectHandlers.forEach(fn => fn())).catch(() => {});
      }, 3000);
    });

    this._registerInviteHandlers();

    UI.setConnState('wait');
    this._startPromise = this._startWithRetry();
    return this._startPromise;
  },

  async _startWithRetry() {
    for (let attempt = 0; ; attempt++) {
      try {
        await this.connection.start();
        UI.setConnState('on');
        return;
      } catch (e) {
        if (String(e).includes('401')) { Auth.logout(true); throw e; }
        UI.setConnState('off');
        await new Promise(r => setTimeout(r, Math.min(10000, 1000 * (attempt + 1))));
      }
    }
  },

  on(evt, fn) {
    if (!this.connection) this.connect();
    this.connection.on(evt, fn);
  },

  onReconnected(fn) { this._reconnectHandlers.push(fn); },

  /** Gọi Hub; server trả HubResult { success, code, message, data }. */
  async invoke(method, ...args) {
    await this.connect();
    let result;
    try {
      result = await this.connection.invoke(method, ...args);
    } catch (e) {
      throw new ApiError('HUB_ERROR', 'Mất kết nối tới máy chủ, đang thử kết nối lại…', 0);
    }
    if (result && result.success === false) throw new ApiError(result.code, result.message, 400);
    return result?.data;
  },

  // ------------------------------------------------------------ lời mời (dùng chung mọi trang)

  _registerInviteHandlers() {
    this.connection.on('INVITE_RECEIVED', invite => this._showInvite(invite));
    this.connection.on('INVITE_UPDATED', u => {
      const box = document.querySelector(`[data-invite="${u.inviteId}"]`);
      if (box && u.status !== 'PENDING') box.close?.(null);
      const me = Auth.user?.nickname;
      if (u.fromNickname === me) {
        const text = {
          ACCEPTED: `${u.toNickname} đã nhận lời mời.`,
          DECLINED: `${u.toNickname} đã từ chối lời mời.`,
          EXPIRED: `Lời mời gửi ${u.toNickname} đã hết hạn.`,
          PENDING: `Đã gửi lời mời tới ${u.toNickname}.`
        }[u.status];
        if (text) UI.toast(text, u.status === 'ACCEPTED' ? 'success' : 'info');
      }
    });
  },

  async _showInvite(invite) {
    Sound.play('notify');
    let timer;
    const choice = await UI.modal({
      title: 'Lời mời chơi cờ',
      html: `<p><b>${UI.escape(invite.fromNickname)}</b> mời bạn vào phòng <b class="mono-num">${invite.code}</b>.</p>
             <p class="muted">${UI.timeConfig(invite)}. Lời mời hết hạn sau <b id="inviteLeft">30</b> giây.</p>`,
      buttons: [
        { label: 'Từ chối', value: false, kind: 'ghost' },
        { label: 'Vào phòng', value: true, kind: 'primary' }
      ],
      onOpen: (m, close) => {
        m.dataset.invite = invite.inviteId;
        m.close = close;
        const tick = () => {
          const left = Math.max(0, Math.round((new Date(invite.expiresAt) - new Date()) / 1000));
          const span = m.querySelector('#inviteLeft');
          if (span) span.textContent = left;
          if (left <= 0) close(null);
        };
        tick();
        timer = setInterval(tick, 500);
      }
    });
    clearInterval(timer);
    if (choice === null) return;
    try {
      const data = await Realtime.invoke('RespondInvite', invite.inviteId, choice);
      if (choice && data?.code) location.href = '/room.html?code=' + data.code;
    } catch (e) {
      UI.error(e);
    }
  }
};
