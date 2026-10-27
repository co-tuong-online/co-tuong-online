// landing.js — bàn cờ ở trang chủ tự đánh lại khai cuộc "Thuận pháo".

(() => {
  if (Auth.isLoggedIn) {
    $('#authLinks').innerHTML = `<a class="btn primary" href="/lobby.html">Vào sảnh</a>`;
    $('#heroActions').innerHTML = `<a class="btn primary big" href="/lobby.html">Vào sảnh chơi</a>
      <a class="btn big" href="/ai-game.html">Đánh với máy</a>`;
  }

  // [từ hàng, từ cột, tới hàng, tới cột, ký hiệu] — đã kiểm tra hợp lệ bằng engine của server
  const opening = [
    [7, 7, 7, 4, 'Pháo 2 bình 5'], [2, 7, 2, 4, 'Pháo 8 bình 5'],
    [9, 7, 7, 6, 'Mã 2 tiến 3'], [0, 7, 2, 6, 'Mã 8 tiến 7'],
    [9, 8, 9, 7, 'Xe 1 bình 2'], [0, 8, 0, 7, 'Xe 9 bình 8'],
    [9, 7, 3, 7, 'Xe 2 tiến 6'], [0, 1, 2, 2, 'Mã 2 tiến 3'],
    [9, 1, 7, 2, 'Mã 8 tiến 7'], [0, 0, 0, 1, 'Xe 1 bình 2'],
    [9, 0, 9, 1, 'Xe 9 bình 8'], [3, 6, 4, 6, 'Tốt 7 tiến 1']
  ];

  const board = new XiangqiBoard($('#heroBoard'));
  const ticker = $('#ticker');
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  let pos = XiangqiBoard.INITIAL.map(r => r.slice());
  let i = 0;

  const step = () => {
    if (i >= opening.length) {
      i = 0;
      pos = XiangqiBoard.INITIAL.map(r => r.slice());
      board.setPosition(pos, { lastMove: null });
      ticker.textContent = '';
      return setTimeout(step, 1600);
    }
    const [fr, fc, tr, tc, text] = opening[i];
    pos[tr][tc] = pos[fr][fc];
    pos[fr][fc] = null;
    board.setPosition(pos, { lastMove: { from: { row: fr, col: fc }, to: { row: tr, col: tc } }, animate: true });
    const n = Math.floor(i / 2) + 1;
    ticker.innerHTML = `<b>${n}.</b> ${i % 2 === 0 ? 'Đỏ' : 'Đen'}: ${UI.escape(text)}`;
    i++;
    setTimeout(step, i >= opening.length ? 3200 : 1300);
  };
  if (!reduceMotion) setTimeout(step, 900);
})();
