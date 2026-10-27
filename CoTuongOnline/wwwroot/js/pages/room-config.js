// room-config.js — hộp thoại chọn cấu hình phòng (dùng ở sảnh khi tạo phòng và trong phòng khi chủ phòng đổi cấu hình).

const RoomConfig = {
  TOTALS: [3, 5, 10, 15, 20, 30],
  MOVES: [30, 60, 90, 120],

  segmented(container, value) {
    $$('button', container).forEach(b => {
      b.setAttribute('aria-pressed', String(b.dataset.value === String(value)));
      b.addEventListener('click', () => {
        $$('button', container).forEach(x => x.setAttribute('aria-pressed', 'false'));
        b.setAttribute('aria-pressed', 'true');
      });
    });
  },

  picked(container) { return $('[aria-pressed="true"]', container)?.dataset.value; },

  /** Mở hộp thoại; trả về config hoặc null nếu huỷ. */
  open({ title = 'Tạo phòng', okLabel = 'Tạo phòng', config = {} } = {}) {
    const c = { ownerColor: 'RANDOM', totalTimeMin: 10, moveTimeSec: 60, isPrivate: false, ...config };
    const seg = (id, items) => `<div class="segmented" id="${id}">${items.map(([v, l]) => `<button type="button" data-value="${v}">${l}</button>`).join('')}</div>`;
    return UI.modal({
      title,
      html: `
        <div class="stack">
          <div><p class="muted" style="margin-bottom:6px">Bạn cầm quân</p>
            ${seg('cfgColor', [['RED', 'Đỏ'], ['BLACK', 'Đen'], ['RANDOM', 'Ngẫu nhiên']])}</div>
          <div><p class="muted" style="margin-bottom:6px">Tổng thời gian mỗi bên</p>
            ${seg('cfgTotal', RoomConfig.TOTALS.map(m => [m, m + ' phút']))}</div>
          <div><p class="muted" style="margin-bottom:6px">Thời gian tối đa cho một nước</p>
            ${seg('cfgMove', RoomConfig.MOVES.map(s => [s, s + ' giây']))}</div>
          <label class="row" style="gap:8px;cursor:pointer">
            <input type="checkbox" id="cfgPrivate" ${c.isPrivate ? 'checked' : ''}>
            <span>Phòng riêng: không hiện ở sảnh, chỉ vào bằng mã, link hoặc lời mời</span>
          </label>
        </div>`,
      buttons: [
        { label: 'Huỷ', value: null, kind: 'ghost' },
        {
          label: okLabel, kind: 'primary',
          value: m => ({
            ownerColor: RoomConfig.picked($('#cfgColor', m)),
            totalTimeMin: Number(RoomConfig.picked($('#cfgTotal', m))),
            moveTimeSec: Number(RoomConfig.picked($('#cfgMove', m))),
            isPrivate: $('#cfgPrivate', m).checked
          })
        }
      ],
      onOpen: m => {
        RoomConfig.segmented($('#cfgColor', m), c.ownerColor);
        RoomConfig.segmented($('#cfgTotal', m), c.totalTimeMin);
        RoomConfig.segmented($('#cfgMove', m), c.moveTimeSec);
      }
    });
  }
};
