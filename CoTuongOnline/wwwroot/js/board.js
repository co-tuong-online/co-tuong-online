// board.js — vẽ bàn cờ tướng và xử lý chọn / kéo-thả quân.
//
// Toạ độ dữ liệu: { row, col }, row 0 = phía Đen (trên), row 9 = phía Đỏ (dưới), col 0 = cột trái nhìn từ phía Đỏ.
// Khi người chơi cầm Đen, board.setFlipped(true) chỉ đảo cách HIỂN THỊ; dữ liệu gửi lên server giữ nguyên.
//
// Dùng:
//   const board = new XiangqiBoard(document.querySelector('#board'), { onMove: (from, to) => {...} });
//   board.setPosition(state.board, { lastMove, animate: true });
//   board.setLegalMoves(legalMoves); board.setMyColor('red'); board.setInteractive(true);

class XiangqiBoard {
  static INITIAL = [
    ['bR', 'bN', 'bB', 'bA', 'bK', 'bA', 'bB', 'bN', 'bR'],
    [null, null, null, null, null, null, null, null, null],
    [null, 'bC', null, null, null, null, null, 'bC', null],
    ['bP', null, 'bP', null, 'bP', null, 'bP', null, 'bP'],
    [null, null, null, null, null, null, null, null, null],
    [null, null, null, null, null, null, null, null, null],
    ['rP', null, 'rP', null, 'rP', null, 'rP', null, 'rP'],
    [null, 'rC', null, null, null, null, null, 'rC', null],
    [null, null, null, null, null, null, null, null, null],
    ['rR', 'rN', 'rB', 'rA', 'rK', 'rA', 'rB', 'rN', 'rR']
  ];

  static GLYPHS = {
    r: { K: '帥', A: '仕', B: '相', N: '傌', R: '俥', C: '炮', P: '兵' },
    b: { K: '將', A: '士', B: '象', N: '馬', R: '車', C: '砲', P: '卒' }
  };
  static VIET = { K: 'Tướng', A: 'Sĩ', B: 'Tượng', N: 'Mã', R: 'Xe', C: 'Pháo', P: 'Tốt' };

  static get pieceStyle() { return localStorage.getItem('ct.pieceStyle') || 'han'; }
  static set pieceStyle(v) { localStorage.setItem('ct.pieceStyle', v); }

  constructor(container, { onMove = null, mini = false, showFiles = !mini } = {}) {
    this.container = container;
    this.onMove = onMove;
    this.mini = mini;
    this.flipped = false;
    this.myColor = null;        // 'red' | 'black' | null (null = xem, không đi được)
    this.interactive = false;
    this.board = XiangqiBoard.INITIAL.map(r => r.slice());
    this.legal = [];
    this.lastMove = null;
    this.checkColor = null;
    this.selected = null;
    this.pieces = new Map();    // "row,col" → element
    this._drag = null;

    container.innerHTML = '';
    container.classList.add('xq-wrap');
    if (showFiles) {
      this.filesTop = this._files('top');
      container.appendChild(this.filesTop);
    }
    this.el = document.createElement('div');
    this.el.className = 'xq-board' + (mini ? ' mini' : '');
    this.el.setAttribute('role', 'grid');
    this.el.setAttribute('aria-label', 'Bàn cờ tướng');
    container.appendChild(this.el);
    if (showFiles) {
      this.filesBottom = this._files('bottom');
      container.appendChild(this.filesBottom);
    }

    this.el.appendChild(this._buildGrid());
    this.markLayer = document.createElement('div');
    this.markLayer.className = 'xq-layer';
    this.pieceLayer = document.createElement('div');
    this.pieceLayer.className = 'xq-layer';
    this.el.append(this.markLayer, this.pieceLayer);

    if (!mini) this._bindPointer();
    this._renderPieces(null);
    this._renderMarks();
  }

  // ================================================================ API công khai

  setPosition(board, { lastMove = this.lastMove, animate = false, check = undefined } = {}) {
    const moveToAnimate = animate && lastMove ? lastMove : null;
    this.board = board.map(r => r.slice());
    this.lastMove = lastMove;
    if (check !== undefined) this.checkColor = check;
    this.selected = null;
    this._renderPieces(moveToAnimate);
    this._renderMarks();
  }

  setLegalMoves(list) {
    this.legal = list || [];
    if (this.selected && !this._targetsFrom(this.selected).length) this.selected = null;
    this._renderMarks();
    this._updateMineClass();
  }

  setMyColor(color) {
    this.myColor = color;
    this._updateMineClass();
  }

  setInteractive(on) {
    this.interactive = on;
    this.el.classList.toggle('disabled', !on);
    if (!on) this.selected = null;
    this._renderMarks();
    this._updateMineClass();
  }

  setFlipped(flipped) {
    if (this.flipped === flipped) return;
    this.flipped = flipped;
    this.el.querySelector('svg').replaceWith(this._buildGrid());
    for (const [key, el] of this.pieces) {
      const [r, c] = key.split(',').map(Number);
      this._place(el, r, c);
    }
    this._renderMarks();
  }

  /** Tướng của bên nào đang bị chiếu ('red' | 'black' | null). */
  setCheck(color) {
    this.checkColor = color;
    this._renderPieces(null);
  }

  /** Đi thử trên giao diện ngay khi người chơi thả quân (trước khi server xác nhận). */
  previewMove(from, to) {
    const next = this.board.map(r => r.slice());
    next[to.row][to.col] = next[from.row][from.col];
    next[from.row][from.col] = null;
    this.legal = [];
    this.setPosition(next, { lastMove: { from, to }, animate: true, check: null });
  }

  pieceAt(p) { return this.board[p.row]?.[p.col] ?? null; }

  // ================================================================ vẽ lưới (SVG 900 x 1000, giao điểm cách nhau 100)

  _buildGrid() {
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 900 1000');
    svg.setAttribute('class', 'xq-grid');
    svg.setAttribute('aria-hidden', 'true');
    const add = (tag, attrs, text) => {
      const n = document.createElementNS(ns, tag);
      for (const k in attrs) n.setAttribute(k, attrs[k]);
      if (text) n.textContent = text;
      svg.appendChild(n);
      return n;
    };
    const X = c => 50 + c * 100, Y = r => 50 + r * 100;

    add('rect', { x: 34, y: 34, width: 832, height: 932, 'stroke-width': 2, rx: 4 });
    add('rect', { x: 50, y: 50, width: 800, height: 900, 'stroke-width': 3.5 });
    for (let r = 1; r < 9; r++) add('line', { x1: X(0), y1: Y(r), x2: X(8), y2: Y(r), 'stroke-width': 2 });
    for (let c = 1; c < 8; c++) {
      add('line', { x1: X(c), y1: Y(0), x2: X(c), y2: Y(4), 'stroke-width': 2 });
      add('line', { x1: X(c), y1: Y(5), x2: X(c), y2: Y(9), 'stroke-width': 2 });
    }
    // cung Tướng
    for (const top of [0, 7]) {
      add('line', { x1: X(3), y1: Y(top), x2: X(5), y2: Y(top + 2), 'stroke-width': 2 });
      add('line', { x1: X(5), y1: Y(top), x2: X(3), y2: Y(top + 2), 'stroke-width': 2 });
    }
    // dấu vị trí Pháo và Tốt
    const marks = [[2, 1], [2, 7], [7, 1], [7, 7], ...[0, 2, 4, 6, 8].flatMap(c => [[3, c], [6, c]])];
    for (const [r, c] of marks) {
      const x = X(c), y = Y(r), g = 9, l = 18;
      const corners = [];
      if (c > 0) corners.push([-1, -1], [-1, 1]);
      if (c < 8) corners.push([1, -1], [1, 1]);
      for (const [dx, dy] of corners) {
        add('path', {
          d: `M ${x + dx * g} ${y + dy * (g + l)} L ${x + dx * g} ${y + dy * g} L ${x + dx * (g + l)} ${y + dy * g}`,
          'stroke-width': 2
        });
      }
    }
    // sông
    const left = this.flipped ? '漢 界' : '楚 河';
    const right = this.flipped ? '楚 河' : '漢 界';
    add('text', { x: X(2), y: 518, 'text-anchor': 'middle', class: 'river-text' }, left);
    add('text', { x: X(6), y: 518, 'text-anchor': 'middle', class: 'river-text' }, right);
    return svg;
  }

  _files(pos) {
    const div = document.createElement('div');
    div.className = 'xq-files ' + pos;
    div.setAttribute('aria-hidden', 'true');
    // Cột đánh số 1–9 từ phải sang trái theo góc nhìn mỗi bên: phía dưới luôn 9…1, phía trên luôn 1…9
    for (let i = 0; i < 9; i++) {
      const s = document.createElement('span');
      s.textContent = pos === 'bottom' ? 9 - i : i + 1;
      div.appendChild(s);
    }
    return div;
  }

  // ================================================================ quân cờ

  _screen(row, col) {
    const sr = this.flipped ? 9 - row : row;
    const sc = this.flipped ? 8 - col : col;
    return { left: ((50 + sc * 100) / 900) * 100, top: ((50 + sr * 100) / 1000) * 100 };
  }

  _place(el, row, col) {
    const p = this._screen(row, col);
    el.style.left = p.left + '%';
    el.style.top = p.top + '%';
  }

  _makePiece(code) {
    const color = code[0] === 'r' ? 'red' : 'black';
    const type = code[1];
    const viet = XiangqiBoard.pieceStyle === 'viet';
    const el = document.createElement('div');
    el.className = `xq-piece ${color}${viet ? ' viet' : ''}`;
    el.dataset.code = code;
    const label = viet ? XiangqiBoard.VIET[type] : XiangqiBoard.GLYPHS[code[0]][type];
    el.innerHTML = `<span>${label}</span>`;
    el.setAttribute('aria-label', `${XiangqiBoard.VIET[type]} ${color === 'red' ? 'Đỏ' : 'Đen'}`);
    return el;
  }

  _renderPieces(animMove) {
    const next = new Map();

    // 1) Di chuyển quân vừa đi (để có hiệu ứng trượt)
    if (animMove) {
      const fromKey = `${animMove.from.row},${animMove.from.col}`;
      const toKey = `${animMove.to.row},${animMove.to.col}`;
      const moving = this.pieces.get(fromKey);
      const code = this.board[animMove.to.row][animMove.to.col];
      if (moving && moving.dataset.code === code) {
        const captured = this.pieces.get(toKey);
        if (captured && captured !== moving) {
          captured.style.opacity = '0';
          captured.style.transition = 'opacity .25s';
          setTimeout(() => captured.remove(), 260);
          this.pieces.delete(toKey);
        }
        this.pieces.delete(fromKey);
        this._place(moving, animMove.to.row, animMove.to.col);
        moving.style.zIndex = 5;
        setTimeout(() => { moving.style.zIndex = ''; }, 300);
        next.set(toKey, moving);
      }
    }

    // 2) Đối chiếu từng ô với thế cờ mới
    for (let r = 0; r < 10; r++) {
      for (let c = 0; c < 9; c++) {
        const key = `${r},${c}`;
        if (next.has(key)) continue;
        const code = this.board[r][c];
        const existing = this.pieces.get(key);
        if (code && existing && existing.dataset.code === code) {
          next.set(key, existing);
          this.pieces.delete(key);
        } else if (code) {
          const el = this._makePiece(code);
          el.style.transition = 'none';
          this._place(el, r, c);
          this.pieceLayer.appendChild(el);
          requestAnimationFrame(() => { el.style.transition = ''; });
          next.set(key, el);
        }
      }
    }
    for (const el of this.pieces.values()) el.remove(); // quân không còn trên bàn
    this.pieces = next;

    // 3) Trạng thái phụ: bị chiếu, quân của mình
    for (const [key, el] of this.pieces) {
      const code = el.dataset.code;
      el.classList.toggle('in-check', code[1] === 'K' && this.checkColor && (code[0] === 'r') === (this.checkColor === 'red'));
      el.classList.toggle('selected', !!this.selected && key === `${this.selected.row},${this.selected.col}`);
    }
    this._updateMineClass();
  }

  _updateMineClass() {
    for (const [key, el] of this.pieces) {
      const [r, c] = key.split(',').map(Number);
      const mine = this.interactive && this._isMine(el.dataset.code) && this._targetsFrom({ row: r, col: c }).length > 0;
      el.classList.toggle('mine', mine);
    }
  }

  _isMine(code) { return !!code && !!this.myColor && (code[0] === 'r') === (this.myColor === 'red'); }

  _targetsFrom(p) {
    return this.legal.filter(m => m.from.row === p.row && m.from.col === p.col).map(m => m.to);
  }

  _isLegal(from, to) {
    return this.legal.some(m => m.from.row === from.row && m.from.col === from.col && m.to.row === to.row && m.to.col === to.col);
  }

  // ================================================================ dấu hiệu: nước vừa đi, ô có thể đi

  _renderMarks() {
    this.markLayer.innerHTML = '';
    const mark = (cls, p) => {
      const d = document.createElement('div');
      d.className = 'xq-mark ' + cls;
      this._place(d, p.row, p.col);
      this.markLayer.appendChild(d);
    };
    if (this.lastMove) {
      mark('last-from', this.lastMove.from);
      mark('last-to', this.lastMove.to);
    }
    if (this.selected && this.interactive) {
      for (const t of this._targetsFrom(this.selected)) mark(this.pieceAt(t) ? 'capture' : 'target', t);
    }
    for (const [key, el] of this.pieces) {
      el.classList.toggle('selected', !!this.selected && key === `${this.selected.row},${this.selected.col}`);
    }
  }

  // ================================================================ chuột / cảm ứng

  _pointFromEvent(e) {
    const rect = this.el.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width) * 900;
    const y = ((e.clientY - rect.top) / rect.height) * 1000;
    let sc = Math.round((x - 50) / 100), sr = Math.round((y - 50) / 100);
    if (sc < 0 || sc > 8 || sr < 0 || sr > 9) return null;
    const dx = x - (50 + sc * 100), dy = y - (50 + sr * 100);
    if (Math.hypot(dx, dy) > 52) return null;
    return this.flipped ? { row: 9 - sr, col: 8 - sc } : { row: sr, col: sc };
  }

  _bindPointer() {
    this.el.addEventListener('pointerdown', e => {
      if (!this.interactive || e.button > 0) return;
      const p = this._pointFromEvent(e);
      if (!p) { this._select(null); return; }

      if (this.selected && this._isLegal(this.selected, p)) {
        e.preventDefault();
        this._commit(this.selected, p);
        return;
      }
      const code = this.pieceAt(p);
      if (this._isMine(code) && this._targetsFrom(p).length) {
        e.preventDefault();
        this._select(p);
        const el = this.pieces.get(`${p.row},${p.col}`);
        this._drag = { from: p, el, startX: e.clientX, startY: e.clientY, active: false, id: e.pointerId };
        this.el.setPointerCapture(e.pointerId);
      } else {
        this._select(null);
      }
    });

    this.el.addEventListener('pointermove', e => {
      const d = this._drag;
      if (!d || d.id !== e.pointerId) return;
      if (!d.active && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) < 6) return;
      d.active = true;
      d.el.classList.add('dragging');
      const rect = this.el.getBoundingClientRect();
      d.el.style.left = ((e.clientX - rect.left) / rect.width) * 100 + '%';
      d.el.style.top = ((e.clientY - rect.top) / rect.height) * 100 + '%';
    });

    const finish = e => {
      const d = this._drag;
      if (!d || d.id !== e.pointerId) return;
      this._drag = null;
      try { this.el.releasePointerCapture(e.pointerId); } catch { /* đã nhả */ }
      if (!d.active) return; // chỉ là click chọn quân
      d.el.classList.remove('dragging');
      const p = e.type === 'pointerup' ? this._pointFromEvent(e) : null;
      if (p && this._isLegal(d.from, p)) {
        d.el.style.transition = 'none';
        this._place(d.el, p.row, p.col);
        requestAnimationFrame(() => { d.el.style.transition = ''; });
        this._commit(d.from, p);
      } else {
        this._place(d.el, d.from.row, d.from.col); // trả quân về chỗ cũ
      }
    };
    this.el.addEventListener('pointerup', finish);
    this.el.addEventListener('pointercancel', finish);
  }

  _select(p) {
    this.selected = p;
    this._renderMarks();
  }

  _commit(from, to) {
    const capture = !!this.pieceAt(to);
    this.selected = null;
    this.previewMove(from, to);
    Sound.play(capture ? 'capture' : 'move');
    this.onMove?.(from, to);
  }
}
