// lobby.js — sảnh: tạo phòng, vào phòng bằng mã, danh sách phòng chờ, chơi với máy.

(async () => {
  if (!Auth.requireLogin()) return;
  UI.renderTopbar('lobby');
  Realtime.connect(); // để nhận lời mời vào phòng

  const user = Auth.user;
  $('#greeting').textContent = `Chào ${user.nickname}`;
  Api.get('/auth/me').then(me => { Auth.updateUser(me); UI.renderStats(me); }).catch(() => {});

  // ---------------------------------------------------- đang ở phòng nào đó?
  try {
    const current = await Api.get('/rooms/current');
    if (current) {
      const playing = current.status === 'PLAYING';
      const banner = $('#resumeBanner');
      banner.innerHTML = `
        <div class="grow"><b>${playing ? 'Bạn có ván đang diễn ra' : 'Bạn vẫn đang ở trong phòng'} ${current.code}.</b>
          <span class="muted">${playing ? 'Quay lại ngay, mất kết nối quá 60 giây sẽ bị xử thua.' : 'Quay lại để tiếp tục chờ đối thủ.'}</span></div>
        <a class="btn ${playing ? 'primary' : 'gold'}" href="/room.html?code=${current.code}">Quay lại phòng</a>
        ${playing ? '' : '<button class="btn ghost" id="leaveOldRoom">Rời phòng</button>'}`;
      banner.classList.remove('hidden');
      $('#leaveOldRoom')?.addEventListener('click', async () => {
        try { await Api.post(`/rooms/${current.code}/leave`); banner.classList.add('hidden'); }
        catch (e) { UI.error(e); }
      });
    }
  } catch { /* bỏ qua */ }

  // ---------------------------------------------------- tạo phòng / vào bằng mã
  $('#createRoomBtn').addEventListener('click', async () => {
    const config = await RoomConfig.open();
    if (!config) return;
    try {
      const room = await Api.post('/rooms', { config });
      location.href = '/room.html?code=' + room.code;
    } catch (e) { UI.error(e); }
  });

  const code = UI.codeBoxes($('#joinCode'), 6, () => $('#joinForm').requestSubmit());
  $('#joinForm').addEventListener('submit', async e => {
    e.preventDefault();
    const c = code();
    if (c.length !== 6) return UI.toast('Mã phòng gồm 6 chữ số.', 'error');
    const btn = $('#joinForm [type=submit]');
    await UI.busy(btn, async () => {
      try {
        await Api.post(`/rooms/${c}/join`);
        location.href = '/room.html?code=' + c;
      } catch (err) { UI.error(err); code.clear(); }
    });
  });

  // ---------------------------------------------------- chơi với máy
  const segs = ['#aiLevel', '#aiColor'].map(id => $(id));
  segs.forEach(s => RoomConfig.segmented(s, RoomConfig.picked(s)));
  $('#startAiBtn').addEventListener('click', e => UI.busy(e.currentTarget, async () => {
    try {
      const game = await Api.post('/ai-games', { level: RoomConfig.picked($('#aiLevel')), playerColor: RoomConfig.picked($('#aiColor')) });
      location.href = '/ai-game.html?id=' + game.gameId;
    } catch (err) { UI.error(err); }
  }));

  // ---------------------------------------------------- danh sách phòng chờ
  const renderRooms = rooms => {
    $('#roomCount').textContent = rooms.length ? `${rooms.length} phòng` : '';
    const list = $('#roomList');
    if (!rooms.length) {
      list.innerHTML = `<div class="empty">Chưa có phòng nào đang chờ.<br>Tạo phòng và gửi mã cho bạn bè.</div>`;
      return;
    }
    list.innerHTML = rooms.map(r => `
      <div class="room-item">
        <span class="code">${r.code}</span>
        <div class="info"><b>${UI.escape(r.ownerNickname)}</b>
          <small>${UI.timeConfig(r.config)}${r.config.ownerColor === 'RANDOM' ? '' : `, chủ phòng cầm ${r.config.ownerColor === 'RED' ? 'Đỏ' : 'Đen'}`}</small></div>
        <button class="btn small" data-join="${r.code}">Vào</button>
      </div>`).join('');
  };
  $('#roomList').addEventListener('click', async e => {
    const c = e.target.closest('[data-join]')?.dataset.join;
    if (!c) return;
    try { await Api.post(`/rooms/${c}/join`); location.href = '/room.html?code=' + c; }
    catch (err) { UI.error(err); loadRooms(); }
  });
  const loadRooms = () => Api.get('/rooms').then(renderRooms).catch(() => {});

  // ---------------------------------------------------- người đang online
  const loadOnline = () => Api.get('/users/online').then(d => {
    $('#onlineCount').textContent = `${d.count} người`;
    $('#onlineList').innerHTML = d.users.length
      ? d.users.map(u => `<span class="badge ${u.roomCode ? 'warn' : 'ok'}" title="${u.roomCode ? 'Đang ở phòng ' + u.roomCode : 'Đang rảnh'}">${UI.escape(u.nickname)}</span>`).join('')
      : `<span class="muted">Chỉ có bạn. Gửi link trang này cho bạn bè nhé.</span>`;
  }).catch(() => {});

  loadRooms(); loadOnline();
  setInterval(loadRooms, 4000);
  setInterval(loadOnline, 8000);
})();
