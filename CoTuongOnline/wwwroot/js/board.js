const names = { xe: 'Xe', ma: 'Mã', tuong: 'Tượng', si: 'Sĩ', chu_tuong: 'Tướng', phao: 'Pháo', tot: 'Tốt' };
export class ChessBoard {
    constructor(element, onMove) { this.element = element; this.onMove = onMove; this.selected = null; }
    render(state, userId, busy) {
        const ownColor = state.players.find(p => p.id === userId)?.color;
        const canMove = !busy && state.status === 'active' && state.board.turn === ownColor;
        if (!canMove || !state.board.pieces.some(p => p.id === this.selected)) this.selected = null;
        this.element.replaceChildren();
        for (let row = 0; row < 10; row++) for (let col = 0; col < 9; col++) {
            const piece = state.board.pieces.find(p => p.row === row && p.col === col);
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'square'; button.disabled = !canMove;
            button.style.left = `${15.4 + col * (84.5 - 15.4) / 8}%`;
            button.style.top = `${10.6 + row * (89.9 - 10.6) / 9}%`;
            button.setAttribute('aria-label', `${piece ? `${names[piece.type]} ${piece.color === 'do' ? 'đỏ' : 'đen'}` : 'Ô trống'}, hàng ${row + 1}, cột ${col + 1}`);
            if (piece) {
                const image = document.createElement('img'); image.src = `/images/${piece.file}`; image.alt = ''; image.draggable = false;
                button.append(image); button.classList.toggle('selected', piece.id === this.selected);
                button.setAttribute('aria-pressed', String(piece.id === this.selected));
            }
            button.addEventListener('click', () => {
                if (piece?.color === ownColor) {
                    this.selected = this.selected === piece.id ? null : piece.id;
                    this.render(state, userId, busy);
                    this.element.children[row * 9 + col]?.focus();
                } else if (this.selected) this.onMove(this.selected, row, col);
            });
            this.element.append(button);
        }
    }
}
