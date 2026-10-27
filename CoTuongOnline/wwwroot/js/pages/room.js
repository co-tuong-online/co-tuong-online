// room.js — Phòng chờ + bàn cờ online + chat trong CÙNG một trang (để SignalR và video call không bị ngắt).

(async () => {
  if (!Auth.requireLogin()) return;
  UI.renderTopbar('lobby');

  const me = Auth.user;
  const code = new URLSearchParams(location.search).get('code') || '';

  const S = {
    room: null,         // RoomDto mới nhất
    game: null,         // GameStateDto của ván hiện tại (kể cả vừa kết thúc)
    myColor: null,
    clock: null,        // { redMs, blackMs, moveTimeSec, turnElapsedMs, at }
    pending: null,      // nước đang chờ server xác nhận
    offline: null,      // { nickname, forfeitAt }
    overlayHidden: false
  };

  const board = new XiangqiBoard($('#board'), { onMove: (from, to) => sendMove(from, to) });

  const fatal = msg => {
    $('#gameLayout').classList.add('hidden');
    $('#fatalMsg').textContent = msg;
    $('#fatal').classList.remove('hidden');
  };
  if (!/^\d{6}$/.test(code)) return fatal('Đường link phòng không hợp lệ. Mã phòng gồm 6 chữ số.');
  $('#roomCodeTitle').textContent = code;
  document.title = `Phòng ${code} · Cờ Tướng Online`;

  // ================================================================== vào phòng

  const isMember = r => r && (r.ownerId === me.id || r.guest?.userId === me.id);
  try {
    let room = await Api.get(`/rooms/${code}`);
    if (!isMember(room)) room = await Api.post(`/rooms/${code}/join`); // vào bằng link / mã
    S.room = room;
  } catch (e) {
    return fatal(e.message);
  }

  registerEvents();
  try {
    await Realtime.connect();
    await Realtime.invoke('JoinRoom', code);
  } catch (e) {
    UI.error(e);
  }
  Realtime.onReconnected(async () => {
    try {
      const room = await Realtime.invoke('JoinRoom', code); // connectionId mới → vào lại Group
      applyRoom(room);
      if (room.gameId) await loadGame(room.gameId);
    } catch (e) { UI.error(e); }
  });

  VideoCall.init(code);
  loadChat();
  applyRoom(S.room);
  if (S.room.status === 'PLAYING' && S.room.gameId) await loadGame(S.room.gameId);
  else { renderPlayers(); renderMoves(); }

  setInterval(tickClocks, 200);

  // ================================================================== sự kiện SignalR

  function registerEvents() {
    Realtime.on('ROOM_UPDATED', room => { if (room.code === code) applyRoom(room); });

    Realtime.on('PLAYER_JOINED', p => {
      if (p.player.userId === me.id) return;
      Sound.play('notify');
      UI.toast(`${p.player.nickname} đã vào phòng.`, 'success');
      chatSystem(`${p.player.nickname} đã vào phòng`);
    });

    Realtime.on('PLAYER_LEFT', p => {
      const name = nicknameOf(p.userId) || 'Đối thủ';
      chatSystem(`${name} đã rời phòng`);
      if (p.userId !== me.id) UI.toast(`${name} đã rời phòng.`);
      if (p.newOwnerId === me.id && S.room?.ownerId !== me.id) UI.toast('Bạn trở thành chủ phòng.', 'success');
      VideoCall.peerLeft();
    });

    Realtime.on('KICKED', async () => {
      VideoCall.hangup(false);
      await UI.modal({ title: 'Bạn đã bị mời ra', html: '<p class="muted">Chủ phòng đã mời bạn ra khỏi phòng này.</p>', buttons: [{ label: 'Về sảnh', value: true, kind: 'primary' }], dismissible: false });
      location.href = '/lobby.html';
    });

    Realtime.on('GAME_STARTED', dto => {
      S.overlayHidden = false;
      applyGame(dto);
      Sound.play('notify');
      const msg = S.myColor === 'red' ? 'Ván bắt đầu. Bạn cầm quân Đỏ, đi trước.' : 'Ván bắt đầu. Bạn cầm quân Đen, Đỏ đi trước.';
      UI.toast(msg, 'success');
      chatSystem('Ván cờ bắt đầu');
    });

    Realtime.on('MOVE_ACCEPTED', onMoveAccepted);

    Realtime.on('MOVE_REJECTED', p => rejectPending(p.reason));

    Realtime.on('GAME_ENDED', onGameEnded);

    Realtime.on('PLAYER_OFFLINE', p => {
      if (p.userId === me.id) return;
      S.offline = { nickname: p.nickname, forfeitAt: new Date(p.forfeitAt) };
      renderOffline();
    });

    Realtime.on('PLAYER_ONLINE', p => {
      if (p.userId === me.id) return;
      S.offline = null;
      renderOffline();
      UI.toast(`${p.nickname} đã quay lại.`, 'success');
    });

    Realtime.on('CHAT_MESSAGE', m => { if (m.code === code) appendChat(m); });
  }

  // ================================================================== phòng

  function applyRoom(room) {
    S.room = room;
    const statusText = { WAITING: 'Chờ người chơi', READY_CHECK: 'Chờ bắt đầu', PLAYING: 'Đang chơi' }[room.status];
    const badge = $('#roomStatus');
    badge.textContent = statusText;
    badge.className = 'badge ' + (room.status === 'PLAYING' ? 'ok' : 'warn');

    VideoCall.setOpponent(room.ownerId === me.id ? room.guest : room.owner);
    // Đối thủ đã rời phòng sau ván → bỏ thông tin ván cũ, hiển thị theo phòng
    if (S.game && S.game.status !== 'PLAYING' && !room.guest) S.game = null;

    if (room.status === 'PLAYING') {
      $('#waitingOverlay').classList.add('hidden');
      if (room.gameId && S.game?.gameId !== room.gameId) loadGame(room.gameId);
    } else {
      renderWaiting();
    }
    if (!S.game || S.game.status !== 'PLAYING') renderPlayers();
    renderStatus();
    renderButtons();
  }

  function nicknameOf(userId) {
    if (S.room?.owner.userId === userId) return S.room.owner.nickname;
    if (S.room?.guest?.userId === userId) return S.room.guest.nickname;
    if (S.game?.red.userId === userId) return S.game.red.nickname;
    if (S.game?.black.userId === userId) return S.game.black.nickname;
    return null;
  }

  function renderWaiting() {
    const r = S.room;
    const overlay = $('#waitingOverlay');
    overlay.classList.toggle('hidden', S.overlayHidden);
    const isOwner = r.ownerId === me.id;
    const guest = r.guest;
    const link = `${location.origin}/room.html?code=${r.code}`;
    const colorText = { RED: 'Chủ phòng cầm Đỏ', BLACK: 'Chủ phòng cầm Đen', RANDOM: 'Màu quân ngẫu nhiên' }[r.config.ownerColor];
    const cfgLine = `<p class="muted" style="margin:0">${UI.timeConfig(r.config)}. ${colorText}.${r.config.isPrivate ? ' Phòng riêng.' : ''}</p>`;

    let body;
    if (!guest) {
      body = `
        <p class="muted" style="margin:0">Gửi mã này cho bạn bè</p>
        <div class="room-code">${r.code}</div>
        ${cfgLine}
        <div class="row" style="margin:16px 0 4px">
          <button class="btn small" data-act="copy-code">Sao chép mã</button>
          <button class="btn small" data-act="copy-link">Sao chép link</button>
          <button class="btn small" data-act="share">Chia sẻ</button>
        </div>
        <div style="margin-top:14px;text-align:left">
          <label class="field"><span>Mời theo biệt danh</span>
            <input class="input" id="inviteSearch" placeholder="Gõ biệt danh, ví dụ minh_ng" autocomplete="off"></label>
          <div class="search-results" id="inviteResults"></div>
        </div>
        ${isOwner ? '<div class="row" style="margin-top:14px"><button class="btn ghost small" data-act="config">Đổi cấu hình</button></div>' : ''}`;
    } else if (isOwner) {
      body = `
        <h2 style="margin-bottom:6px">${UI.escape(guest.nickname)} đã vào phòng</h2>
        ${cfgLine}
        <p style="margin:14px 0">${r.guestReady
          ? '<span class="badge ok">Đã sẵn sàng</span>'
          : '<span class="badge warn">Chưa sẵn sàng</span>'}</p>
        <div class="row">
          <button class="btn primary big" data-act="start" ${r.guestReady ? '' : 'disabled'}>Bắt đầu ván</button>
        </div>
        <div class="row" style="margin-top:12px">
          <button class="btn ghost small" data-act="config">Đổi cấu hình</button>
          <button class="btn ghost small" data-act="kick">Mời ra khỏi phòng</button>
          ${S.game ? '<button class="btn ghost small" data-act="peek">Xem lại bàn cờ</button>' : ''}
        </div>`;
    } else {
      body = `
        <h2 style="margin-bottom:6px">Phòng của ${UI.escape(r.owner.nickname)}</h2>
        ${cfgLine}
        <p class="muted" style="margin:14px 0">${r.guestReady ? 'Chờ chủ phòng bắt đầu ván…' : 'Bấm Sẵn sàng khi bạn đã chuẩn bị xong.'}</p>
        <div class="row">
          <button class="btn ${r.guestReady ? '' : 'primary'} big" data-act="ready">${r.guestReady ? 'Huỷ sẵn sàng' : 'Sẵn sàng'}</button>
        </div>
        ${S.game ? '<div class="row" style="margin-top:12px"><button class="btn ghost small" data-act="peek">Xem lại bàn cờ</button></div>' : ''}`;
    }
    const card = $('#waitingCard');
    const searchValue = $('#inviteSearch')?.value || '';
    card.innerHTML = body;
    const search = $('#inviteSearch');
    if (search) {
      search.value = searchValue;
      bindInviteSearch(search);
      if (searchValue) search.dispatchEvent(new Event('input'));
    }
  }

  $('#waitingCard').addEventListener('click', async e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const act = btn.dataset.act;
    const link = `${location.origin}/room.html?code=${code}`;
    try {
      if (act === 'copy-code') await copy(code, 'Đã sao chép mã phòng.');
      else if (act === 'copy-link') await copy(link, 'Đã sao chép link phòng.');
      else if (act === 'share') {
        const text = `Vào đánh cờ tướng với mình nhé! Mã phòng ${code}`;
        if (navigator.share) await navigator.share({ title: 'Cờ Tướng Online', text, url: link }).catch(() => {});
        else window.open(`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(link)}`, '_blank', 'noopener');
      }
      else if (act === 'config') {
        const config = await RoomConfig.open({ title: 'Cấu hình phòng', okLabel: 'Lưu cấu hình', config: S.room.config });
        if (config) await Api.put(`/rooms/${code}/config`, config);
      }
      else if (act === 'ready') await UI.busy(btn, () => Api.post(`/rooms/${code}/ready`, { ready: !S.room.guestReady }));
      else if (act === 'start') await UI.busy(btn, () => Api.post(`/rooms/${code}/start`));
      else if (act === 'kick') {
        if (await UI.confirm('Mời ra khỏi phòng', `Mời ${S.room.guest.nickname} ra khỏi phòng?`, 'Mời ra', true))
          await Api.post(`/rooms/${code}/kick`);
      }
      else if (act === 'peek') {
        S.overlayHidden = true;
        $('#waitingOverlay').classList.add('hidden');
        renderButtons();
      }
    } catch (err) { UI.error(err); }
  });

  function bindInviteSearch(input) {
    let timer;
    input.addEventListener('input', () => {
      clearTimeout(timer);
      const q = input.value.trim();
      const box = $('#inviteResults');
      if (!q) { box.innerHTML = ''; return; }
      timer = setTimeout(async () => {
        try {
          const users = await Api.get('/users/search?q=' + encodeURIComponent(q));
          if (!users.length) { box.innerHTML = '<p class="faint" style="margin:6px 2px">Không tìm thấy ai.</p>'; return; }
          box.innerHTML = users.map(u => `
            <div class="item">
              <span class="conn-dot ${u.online ? 'on' : ''}"></span>
              <span class="grow">${UI.escape(u.nickname)} <small class="faint">${u.online ? (u.roomCode ? 'đang ở phòng khác' : 'đang online') : 'offline'}</small></span>
              <button class="btn small" data-invite="${u.id}" data-nick="${UI.escape(u.nickname)}" ${u.online ? '' : 'disabled'}>Mời</button>
            </div>`).join('');
        } catch (e) { UI.error(e); }
      }, 300);
    });
  }

  $('#waitingCard').addEventListener('click', async e => {
    const btn = e.target.closest('[data-invite]');
    if (!btn) return;
    try {
      await UI.busy(btn, () => Realtime.invoke('SendInvite', code, Number(btn.dataset.invite), btn.dataset.nick));
      btn.textContent = 'Đã mời';
      btn.disabled = true;
    } catch (err) { UI.error(err); }
  });

  async function copy(text, okMsg) {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      const ta = UI.el(`<textarea style="position:fixed;opacity:0">${UI.escape(text)}</textarea>`);
      document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove();
    }
    UI.toast(okMsg, 'success');
  }

  // ================================================================== ván cờ

  async function loadGame(gameId) {
    try {
      applyGame(await Api.get(`/games/${gameId}/state`));
    } catch (e) { UI.error(e); }
  }

  function applyGame(dto) {
    S.game = dto;
    S.pending = null;
    S.myColor = dto.red.userId === me.id ? 'red' : dto.black.userId === me.id ? 'black' : null;
    S.clock = { ...dto.state.clock, at: performance.now() };
    S.offline = dto.offlineNickname ? { nickname: dto.offlineNickname, forfeitAt: new Date(dto.forfeitAt) } : null;

    board.setFlipped(S.myColor === 'black');
    board.setMyColor(S.myColor);
    const last = dto.state.moves.at(-1);
    board.setPosition(dto.state.board, {
      lastMove: last ? { from: last.from, to: last.to } : null,
      check: dto.inCheck ? dto.state.sideToMove : null
    });
    updateInteractivity();
    renderMoves();
    renderPlayers();
    renderOffline();
    renderStatus();
    renderButtons();
    if (dto.status === 'PLAYING') $('#waitingOverlay').classList.add('hidden');
  }

  function myTurn() {
    return S.game?.status === 'PLAYING' && S.game.state.sideToMove === S.myColor && !S.pending;
  }

  function updateInteractivity() {
    const mine = myTurn();
    board.setLegalMoves(mine ? S.game.legalMoves : []);
    board.setInteractive(mine);
  }

  async function sendMove(from, to) {
    if (!myTurn()) return;
    S.pending = { from, to };
    board.setInteractive(false);
    renderStatus();
    try {
      // seq = số thứ tự nửa nước sẽ tạo ra; gửi trùng (bấm 2 lần, mạng chập chờn) sẽ bị server bỏ qua
      await Realtime.invoke('MakeMove', S.game.gameId, from, to, S.game.state.ply + 1);
    } catch (err) {
      rejectPending(err.message);
    }
  }

  function rejectPending(reason) {
    if (!S.pending) return;
    S.pending = null;
    UI.toast(reason || 'Nước đi không hợp lệ.', 'error');
    board.setPosition(S.game.state.board, { animate: false }); // trả quân về chỗ cũ
    updateInteractivity();
    renderStatus();
  }

  function onMoveAccepted(p) {
    if (!S.game || p.gameId !== S.game.gameId) return;
    const st = S.game.state;
    st.board = p.board;
    st.sideToMove = p.sideToMove;
    st.ply = p.ply;
    st.moves.push(p.move);
    S.game.legalMoves = p.legalMoves;
    S.game.inCheck = p.check;
    S.clock = { ...p.clock, at: performance.now() };

    const mine = S.pending && samePt(S.pending.from, p.move.from) && samePt(S.pending.to, p.move.to);
    S.pending = null;
    board.setPosition(p.board, {
      lastMove: { from: p.move.from, to: p.move.to },
      animate: !mine,
      check: p.check ? p.sideToMove : null
    });
    if (!mine) Sound.play(p.move.captured ? 'capture' : 'move');
    if (p.check) setTimeout(() => Sound.play('check'), 120);

    updateInteractivity();
    renderMoves();
    renderPlayers();
    renderStatus();
  }

  async function onGameEnded(p) {
    if (!S.game || p.gameId !== S.game.gameId) return;
    Object.assign(S.game, { status: 'FINISHED', result: p.result, endReason: p.endReason });
    S.clock = { ...p.clock, at: performance.now() };
    S.pending = null;
    S.offline = null;
    board.setInteractive(false);
    renderOffline();
    renderPlayers();
    renderStatus();
    renderButtons();
    const choice = await UI.showResult({
      result: p.result, reason: p.endReason, myColor: S.myColor,
      extraButtons: [
        { label: 'Về sảnh', value: 'lobby', kind: 'ghost' },
        { label: 'Ở lại phòng', value: 'stay', kind: 'primary' }
      ]
    });
    if (choice === 'lobby') {
      await leaveRoom(false);
    }
  }

  // ================================================================== vẽ phần phụ

  function samePt(a, b) { return a.row === b.row && a.col === b.col; }

  function renderPlayers() {
    const g = S.game, r = S.room;
    let top, bottom;
    if (g) {
      const myColor = S.myColor || 'red';
      const opp = myColor === 'red' ? 'black' : 'red';
      top = { ...g[opp], color: opp };
      bottom = { ...g[myColor], color: myColor };
    } else {
      const meP = r.owner.userId === me.id ? r.owner : r.guest;
      const opp = r.owner.userId === me.id ? r.guest : r.owner;
      top = opp ? { ...opp } : null;
      bottom = { ...meP };
    }
    renderCard($('#topPlayer'), top, false);
    renderCard($('#bottomPlayer'), bottom, true);
    tickClocks();
  }

  function renderCard(el, p, isMe) {
    if (!p) {
      el.className = 'player-card';
      el.innerHTML = `<div class="disc none">?</div><div class="who"><div class="name faint">Đang chờ đối thủ…</div><div class="sub">Gửi mã phòng hoặc mời theo biệt danh</div></div>`;
      return;
    }
    const playing = S.game?.status === 'PLAYING';
    const toMove = playing && S.game.state.sideToMove === p.color;
    const glyph = p.color === 'red' ? '帥' : p.color === 'black' ? '將' : '?';
    const online = p.online !== false;
    const isOwner = S.room?.ownerId === p.userId;
    el.className = 'player-card' + (toMove ? ' to-move' : '');
    el.dataset.color = p.color || '';
    el.innerHTML = `
      <div class="disc ${p.color || 'none'}">${p.color ? glyph : '?'}</div>
      <div class="who">
        <div class="name"><span style="overflow:hidden;text-overflow:ellipsis">${UI.escape(p.nickname)}</span> <span class="conn-dot ${online ? 'on' : ''}" title="${online ? 'Online' : 'Mất kết nối'}"></span></div>
        <div class="sub">${[isMe ? 'Bạn' : '', p.color ? 'quân ' + UI.colorName(p.color) : '', isOwner ? 'chủ phòng' : ''].filter(Boolean).join(', ').replace(/^./, c => c.toUpperCase())}</div>
      </div>
      ${p.color ? '<div class="clock"><span class="total">--:--</span><span class="per-move"></span></div>' : ''}`;
  }

  function tickClocks() {
    if (!S.clock || !S.game) return;
    const c = S.clock;
    const running = S.game.status === 'PLAYING';
    const elapsed = running ? c.turnElapsedMs + (performance.now() - c.at) : 0;
    for (const el of [$('#topPlayer'), $('#bottomPlayer')]) {
      const color = el.dataset.color;
      const clock = $('.clock', el);
      if (!color || !clock) continue;
      const toMove = running && S.game.state.sideToMove === color;
      const base = color === 'red' ? c.redMs : c.blackMs;
      const total = toMove ? base - elapsed : base;
      const perMove = c.moveTimeSec * 1000 - elapsed;
      $('.total', clock).textContent = UI.clock(total);
      $('.per-move', clock).textContent = toMove ? `nước này ${UI.clock(perMove)}` : '';
      clock.classList.toggle('low', toMove && (total < 30000 || perMove < 10000));
    }
    if (S.offline) {
      const left = Math.max(0, Math.round((S.offline.forfeitAt - new Date()) / 1000));
      const span = $('#offlineLeft');
      if (span) span.textContent = left;
    }
  }

  function renderOffline() {
    const b = $('#offlineBanner');
    if (!S.offline) { b.classList.add('hidden'); return; }
    b.innerHTML = `<b>${UI.escape(S.offline.nickname)}</b> mất kết nối. Nếu không quay lại trong <b id="offlineLeft">60</b> giây sẽ bị xử thua.`;
    b.classList.remove('hidden');
    tickClocks();
  }

  function renderStatus() {
    const line = $('#statusLine');
    line.classList.remove('attention');
    const g = S.game;
    let text;
    if (g && g.status === 'PLAYING') {
      if (S.pending) text = 'Đang gửi nước đi…';
      else if (g.state.sideToMove === S.myColor) {
        text = g.inCheck ? 'Bạn đang bị chiếu! Hãy đỡ chiếu.' : 'Đến lượt bạn.';
        if (g.inCheck) line.classList.add('attention');
      } else text = g.inCheck ? 'Bạn đang chiếu tướng đối thủ.' : 'Chờ đối thủ đi…';
    } else if (g && g.status === 'FINISHED') {
      const o = UI.outcomeFor(g.result, S.myColor);
      text = `${{ win: 'Bạn thắng', lose: 'Bạn thua', draw: 'Hoà' }[o]} (${UI.endReason(g.endReason)}).`;
    } else if (S.room) {
      text = { WAITING: 'Chờ người chơi thứ hai vào phòng.', READY_CHECK: S.room.guestReady ? 'Hai bên đã sẵn sàng.' : 'Chờ người chơi thứ hai sẵn sàng.' }[S.room.status] || '';
    }
    line.textContent = text || '';
  }

  function renderButtons() {
    const playing = S.game?.status === 'PLAYING';
    $('#resignBtn').classList.toggle('hidden', !playing);
    $('#leaveBtn').textContent = 'Rời phòng';
    if (S.overlayHidden && S.room?.status !== 'PLAYING') {
      let back = $('#backToWaiting');
      if (!back) {
        back = UI.el('<button class="btn gold small" id="backToWaiting">Về phòng chờ</button>');
        back.addEventListener('click', () => { S.overlayHidden = false; renderWaiting(); back.remove(); });
        $('#leaveBtn').after(back);
      }
    } else $('#backToWaiting')?.remove();
  }

  function renderMoves() {
    const moves = S.game?.state.moves || [];
    const box = $('#moveList');
    if (!moves.length) { box.innerHTML = '<div class="empty-line">Chưa có nước nào.</div>'; return; }
    // Gom theo lượt: mỗi hàng 1 nước Đỏ + 1 nước Đen
    const rows = [];
    for (const m of moves) {
      const turn = Math.ceil(m.ply / 2);
      rows[turn] ??= { red: '', black: '' };
      rows[turn][m.piece[0] === 'r' ? 'red' : 'black'] = m.notation;
    }
    box.innerHTML = `<table>${rows.map((r, i) => r ? `<tr><td class="n">${i}.</td><td class="r">${UI.escape(r.red)}</td><td>${UI.escape(r.black)}</td></tr>` : '').join('')}</table>`;
    box.scrollTop = box.scrollHeight;
  }

  // ================================================================== nút

  $('#resignBtn').addEventListener('click', async () => {
    if (!S.game || S.game.status !== 'PLAYING') return;
    if (!await UI.confirm('Đầu hàng', 'Bạn chắc chắn muốn đầu hàng ván này?', 'Đầu hàng', true)) return;
    try { await Realtime.invoke('Resign', S.game.gameId); } catch (e) { UI.error(e); }
  });

  $('#leaveBtn').addEventListener('click', () => leaveRoom(true));

  async function leaveRoom(ask) {
    const playing = S.game?.status === 'PLAYING';
    if (ask && playing && !await UI.confirm('Rời phòng', 'Ván đang diễn ra. Rời phòng sẽ bị xử thua.', 'Rời phòng và chịu thua', true)) return;
    VideoCall.hangup(true);
    window.removeEventListener('beforeunload', beforeUnload);
    try { await Api.post(`/rooms/${code}/leave`); } catch { /* phòng có thể đã đóng */ }
    location.href = '/lobby.html';
  }

  // Cảnh báo khi lỡ tay đóng tab / tải lại trang lúc đang chơi
  function beforeUnload(e) {
    if (S.game?.status === 'PLAYING') { e.preventDefault(); e.returnValue = ''; }
  }
  window.addEventListener('beforeunload', beforeUnload);

  const soundBtn = $('#soundBtn');
  const renderSoundBtn = () => soundBtn.textContent = Sound.enabled ? 'Tắt tiếng' : 'Bật tiếng';
  soundBtn.addEventListener('click', () => { Sound.enabled = !Sound.enabled; renderSoundBtn(); });
  renderSoundBtn();

  const styleBtn = $('#styleBtn');
  const renderStyleBtn = () => styleBtn.textContent = XiangqiBoard.pieceStyle === 'viet' ? 'Quân chữ Hán' : 'Quân chữ Việt';
  styleBtn.addEventListener('click', () => {
    XiangqiBoard.pieceStyle = XiangqiBoard.pieceStyle === 'viet' ? 'han' : 'viet';
    board.pieces.forEach(el => el.remove());
    board.pieces.clear();
    board.setPosition(board.board, {});
    renderStyleBtn();
  });
  renderStyleBtn();

  // ================================================================== chat

  async function loadChat() {
    try {
      const msgs = await Api.get(`/rooms/${code}/messages`);
      $('#chatLog').innerHTML = '';
      msgs.forEach(appendChat);
      if (!msgs.length) chatSystem('Hãy gửi lời chào đối thủ');
    } catch { /* bỏ qua */ }
  }

  function appendChat(m) {
    const log = $('#chatLog');
    const mine = m.userId === me.id;
    log.appendChild(UI.el(`<div class="chat-msg ${mine ? 'me' : ''}"><span class="who">${UI.escape(m.nickname)}</span>${UI.escape(m.content)}</div>`));
    log.scrollTop = log.scrollHeight;
    if (!mine && document.hidden) Sound.play('notify');
  }

  function chatSystem(text) {
    const log = $('#chatLog');
    log.appendChild(UI.el(`<div class="chat-system">${UI.escape(text)}</div>`));
    log.scrollTop = log.scrollHeight;
  }

  $('#chatForm').addEventListener('submit', async e => {
    e.preventDefault();
    const input = $('#chatInput');
    const text = input.value.trim();
    if (!text) return;
    input.value = '';
    try { await Realtime.invoke('SendChat', code, text); }
    catch (err) { UI.error(err); input.value = text; }
  });
})();
