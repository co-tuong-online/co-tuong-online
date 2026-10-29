// ai-game.js — ván với máy. Người đi → POST /moves, sau đó POST /ai-move để máy trả lời.

(async () => {
  if (!Auth.requireLogin()) return;
  UI.renderTopbar('ai');
  Realtime.connect(); // vẫn nhận được lời mời vào phòng khi đang đánh với máy

  const params = new URLSearchParams(location.search);
  const gameId = params.get('id');
  let G = null;           // AiGameDto
  let thinking = false;

  // ------------------------------------------------------------ chọn ván mới
  if (!gameId) {
    $('#setup').classList.remove('hidden');
    RoomConfig.segmented($('#aiLevel'), 'EASY');
    RoomConfig.segmented($('#aiColor'), 'red');
    $('#createBtn').addEventListener('click', e => UI.busy(e.currentTarget, async () => {
      try {
        const g = await Api.post('/ai-games', { level: RoomConfig.picked($('#aiLevel')), playerColor: RoomConfig.picked($('#aiColor')) });
        location.replace('/ai-game.html?id=' + g.gameId);
      } catch (err) { UI.error(err); }
    }));
    return;
  }

  const board = new XiangqiBoard($('#board'), { onMove: (from, to) => playerMove(from, to) });
  $('#gameLayout').classList.remove('hidden');

  try {
    apply(await Api.get('/ai-games/' + gameId), null);
  } catch (e) {
    $('#gameLayout').classList.add('hidden');
    $('#setup').classList.remove('hidden');
    RoomConfig.segmented($('#aiLevel'), 'EASY');
    RoomConfig.segmented($('#aiColor'), 'red');
    UI.toast(e.message + ' Hãy bắt đầu ván mới.', 'error', 6000);
    history.replaceState(null, '', '/ai-game.html');
    $('#createBtn').addEventListener('click', ev => UI.busy(ev.currentTarget, async () => {
      const g = await Api.post('/ai-games', { level: RoomConfig.picked($('#aiLevel')), playerColor: RoomConfig.picked($('#aiColor')) });
      location.replace('/ai-game.html?id=' + g.gameId);
    }));
    return;
  }
  if (G.status !== 'PLAYING') finished(); // mở lại ván đã kết thúc → hiện luôn kết quả
  else maybeAiTurn();

  // ------------------------------------------------------------ hiển thị

  function apply(dto, move, animate = false) {
    G = dto;
    const st = dto.state;
    const last = move || st.moves.at(-1);
    board.setFlipped(dto.playerColor === 'black');
    board.setMyColor(dto.playerColor);
    board.setPosition(st.board, {
      lastMove: last ? { from: last.from, to: last.to } : null,
      animate,
      check: dto.inCheck ? st.sideToMove : null
    });
    const myTurn = dto.status === 'PLAYING' && st.sideToMove === dto.playerColor && !thinking;
    board.setLegalMoves(myTurn ? dto.legalMoves : []);
    board.setInteractive(myTurn);

    $('#gameTitle').textContent = dto.savedGameName ? dto.savedGameName : 'Đánh với máy';
    $('#gameSub').textContent = `Mức ${dto.level === 'HARD' ? 'Khó' : 'Dễ'}, bạn cầm quân ${UI.colorName(dto.playerColor)}`;
    renderCards();
    renderMoves();
    renderStatus();
    $('#undoBtn').textContent = `Đi lại (${dto.undoLeft})`;
    $('#undoBtn').disabled = !dto.canUndo || thinking;
    $('#saveBtn').disabled = dto.status !== 'PLAYING' || thinking;
    $('#resignBtn').disabled = dto.status !== 'PLAYING';
  }

  function renderCards() {
    const ai = G.playerColor === 'red' ? 'black' : 'red';
    const toMove = G.status === 'PLAYING' ? G.state.sideToMove : null;
    const card = (el, color, name, sub) => {
      el.className = 'player-card' + (toMove === color ? ' to-move' : '');
      el.innerHTML = `<div class="disc ${color}">${color === 'red' ? '帥' : '將'}</div>
        <div class="who"><div class="name">${UI.escape(name)}</div><div class="sub">${sub}</div></div>
        ${toMove === color && color === ai && thinking ? '<span class="spinner" style="color:var(--gold)"></span>' : ''}`;
    };
    card($('#aiCard'), ai, `Máy (${G.level === 'HARD' ? 'Khó' : 'Dễ'})`, 'Quân ' + UI.colorName(ai));
    card($('#meCard'), G.playerColor, Auth.user.nickname + ' (bạn)', 'Quân ' + UI.colorName(G.playerColor));
  }

  function renderStatus() {
    const line = $('#statusLine');
    line.classList.remove('attention');
    if (G.status !== 'PLAYING') {
      const o = UI.outcomeFor(G.result, G.playerColor);
      line.textContent = `${{ win: 'Bạn thắng', lose: 'Bạn thua', draw: 'Hoà' }[o]} (${UI.endReason(G.endReason)}).`;
      return;
    }
    if (thinking || G.state.sideToMove !== G.playerColor) {
      line.innerHTML = '<span class="spinner" style="color:var(--gold)"></span> Máy đang nghĩ…';
      return;
    }
    line.textContent = G.inCheck ? 'Bạn đang bị chiếu! Hãy đỡ chiếu.' : 'Đến lượt bạn.';
    if (G.inCheck) line.classList.add('attention');
  }

  function renderMoves() {
    const moves = G.state.moves;
    const box = $('#moveList');
    if (!moves.length) { box.innerHTML = '<div class="empty-line">Chưa có nước nào.</div>'; return; }
    const rows = [];
    for (const m of moves) {
      const turn = Math.ceil(m.ply / 2);
      rows[turn] ??= { red: '', black: '' };
      rows[turn][m.piece[0] === 'r' ? 'red' : 'black'] = m.notation;
    }
    box.innerHTML = `<table>${rows.map((r, i) => r ? `<tr><td class="n">${i}.</td><td class="r">${UI.escape(r.red)}</td><td>${UI.escape(r.black)}</td></tr>` : '').join('')}</table>`;
    box.scrollTop = box.scrollHeight;
  }

  // ------------------------------------------------------------ đi quân

  async function playerMove(from, to) {
    board.setInteractive(false);
    try {
      const res = await Api.post(`/ai-games/${G.gameId}/moves`, { from, to });
      apply(res.game, res.move, false); // quân đã nằm sẵn ở ô mới (previewMove)
      if (res.game.inCheck) Sound.play('check');
      if (res.game.status !== 'PLAYING') return finished();
      maybeAiTurn();
    } catch (err) {
      UI.error(err);
      apply(G, null); // trả quân về chỗ cũ
    }
  }

  async function maybeAiTurn(attempt = 1) {
    if (G.status !== 'PLAYING' || G.state.sideToMove === G.playerColor) return;
    thinking = true;
    apply(G, null);
    const started = performance.now();
    try {
      const res = await Api.post(`/ai-games/${G.gameId}/ai-move`);
      // Máy "nghĩ" tối thiểu 0,5 giây cho tự nhiên
      const wait = 500 - (performance.now() - started);
      if (wait > 0) await new Promise(r => setTimeout(r, wait));
      thinking = false;
      apply(res.game, res.move, true);
      if (res.move) Sound.play(res.move.captured ? 'capture' : 'move');
      if (res.game.inCheck) setTimeout(() => Sound.play('check'), 150);
      if (res.game.status !== 'PLAYING') return finished();
      // Phòng hờ: server không trả nước nào mà vẫn tới lượt máy → thử lại thay vì đứng im
      if (!res.move && res.game.state.sideToMove !== res.game.playerColor) throw new Error('Máy chưa đi được nước nào.');
    } catch (err) {
      thinking = false;
      if (attempt < 3) {
        // Lỗi mạng / server bận: tự thử lại sau 1,5 giây
        setTimeout(() => maybeAiTurn(attempt + 1), 1500);
        return;
      }
      apply(G, null);
      showAiRetry(err.message);
    }
  }

  function showAiRetry(message) {
    const line = $('#statusLine');
    line.classList.add('attention');
    line.innerHTML = `<span class="grow">Máy gặp sự cố khi tính nước${message ? ': ' + UI.escape(message) : '.'}</span>
      <button class="btn small gold" id="aiRetryBtn">Thử lại</button>`;
    $('#aiRetryBtn').addEventListener('click', async () => {
      try { apply(await Api.get('/ai-games/' + G.gameId), null); } catch { /* giữ trạng thái cũ */ }
      if (G.status !== 'PLAYING') return finished();
      maybeAiTurn();
    });
  }

  async function finished() {
    const choice = await UI.showResult({
      result: G.result, reason: G.endReason, myColor: G.playerColor,
      extraButtons: [
        { label: 'Xem lại bàn cờ', value: 'view', kind: 'ghost' },
        { label: 'Ván mới', value: 'new', kind: 'primary' }
      ]
    });
    if (choice === 'new') location.href = '/ai-game.html';
  }

  // ------------------------------------------------------------ nút

  $('#undoBtn').addEventListener('click', async () => {
    try { apply(await Api.post(`/ai-games/${G.gameId}/undo`), null); }
    catch (err) { UI.error(err); }
  });

  $('#resignBtn').addEventListener('click', async () => {
    if (!await UI.confirm('Đầu hàng', 'Bạn muốn chịu thua ván này?', 'Đầu hàng', true)) return;
    try {
      apply(await Api.post(`/ai-games/${G.gameId}/resign`), null);
      finished();
    } catch (err) { UI.error(err); }
  });

  $('#saveBtn').addEventListener('click', async () => {
    const defaultName = G.savedGameName || `Ván với máy ${UI.dateTime(new Date())}`;
    let overwriteId = null;
    if (G.savedGameId) {
      const choice = await UI.modal({
        title: 'Lưu ván',
        html: `<p class="muted">Ván này được mở từ bản lưu "${UI.escape(G.savedGameName)}". Ghi đè bản cũ hay lưu thành bản mới?</p>`,
        buttons: [
          { label: 'Huỷ', value: null, kind: 'ghost' },
          { label: 'Lưu bản mới', value: 'new' },
          { label: 'Ghi đè bản cũ', value: 'overwrite', kind: 'primary' }
        ]
      });
      if (!choice) return;
      if (choice === 'overwrite') overwriteId = G.savedGameId;
    }
    const name = await UI.prompt('Lưu ván', 'Tên ván lưu', overwriteId ? G.savedGameName : defaultName, 'Lưu ván');
    if (!name) return;
    try {
      const saved = await Api.post('/saved-games', { gameId: G.gameId, name, overwriteId });
      G.savedGameId = saved.id;
      G.savedGameName = saved.name;
      $('#gameTitle').textContent = saved.name;
      UI.toast(`Đã lưu "${saved.name}". Mở lại ở mục Ván đã lưu.`, 'success');
    } catch (err) { UI.error(err); }
  });

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
})();
