// saved-games.js — danh sách ván đã lưu: tiếp tục, đổi tên, xoá.

(async () => {
  if (!Auth.requireLogin()) return;
  UI.renderTopbar('saved');
  Realtime.connect();

  const list = $('#savedList');

  async function load() {
    list.innerHTML = '<div class="muted"><span class="spinner"></span> Đang tải…</div>';
    try { render(await Api.get('/saved-games')); }
    catch (e) { list.innerHTML = ''; UI.error(e); }
  }

  function render(items) {
    if (!items.length) {
      list.innerHTML = `<div class="empty" style="grid-column:1/-1">Chưa có ván nào được lưu.<br>
        Khi đánh với máy, bấm <b>Lưu ván</b> để chơi tiếp vào lúc khác.</div>`;
      return;
    }
    list.innerHTML = '';
    for (const s of items) {
      const card = UI.el(`
        <article class="saved-card">
          <div class="thumb"></div>
          <div>
            <h3>${UI.escape(s.name)}</h3>
            <div class="meta">Máy ${s.aiLevel === 'HARD' ? 'Khó' : 'Dễ'}, bạn cầm ${UI.colorName(s.playerColor)}, ${s.moveCount} nước</div>
            <div class="meta">Lưu lúc ${UI.dateTime(s.updatedAt)}</div>
          </div>
          <div class="row">
            <button class="btn primary small" data-act="resume">Chơi tiếp</button>
            <button class="btn ghost small" data-act="rename">Đổi tên</button>
            <button class="btn ghost small" data-act="delete">Xoá</button>
          </div>
        </article>`);
      const mini = new XiangqiBoard($('.thumb', card), { mini: true });
      mini.setFlipped(s.playerColor === 'black');
      mini.setPosition(s.board);
      card.addEventListener('click', e => onAction(e, s));
      list.appendChild(card);
    }
  }

  async function onAction(e, s) {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    try {
      if (btn.dataset.act === 'resume') {
        await UI.busy(btn, async () => {
          const game = await Api.post(`/saved-games/${s.id}/resume`);
          location.href = '/ai-game.html?id=' + game.gameId;
        });
      } else if (btn.dataset.act === 'rename') {
        const name = await UI.prompt('Đổi tên ván lưu', 'Tên mới', s.name, 'Đổi tên');
        if (name && name !== s.name) { await Api.patch(`/saved-games/${s.id}`, { name }); UI.toast('Đã đổi tên.', 'success'); load(); }
      } else if (btn.dataset.act === 'delete') {
        if (await UI.confirm('Xoá ván lưu', `Xoá "${s.name}"? Không thể khôi phục.`, 'Xoá', true)) {
          await Api.del(`/saved-games/${s.id}`);
          UI.toast('Đã xoá ván lưu.', 'success');
          load();
        }
      }
    } catch (err) { UI.error(err); }
  }

  load();
})();
