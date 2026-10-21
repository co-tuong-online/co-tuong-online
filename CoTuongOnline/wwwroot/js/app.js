import { api, resetCsrf } from './api.js';
import { ChessBoard } from './board.js';
const $ = id => document.getElementById(id);
let user, room, connection, register = location.pathname === '/account/register', busy = false;
const board = new ChessBoard($('squares'), move);
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); $('notice').hidden = !message; }
function show(name) { for (const id of ['auth', 'lobby', 'game']) $(id).hidden = name !== id; }
function authScreen() {
    user = null; room = null; $('identity').hidden = true; show('auth');
    $('auth-title').textContent = register ? 'Tạo tài khoản' : 'Đăng nhập';
    $('auth-description').textContent = register ? 'Bắt đầu những ván cờ cùng bạn bè.' : 'Chào mừng bạn trở lại bàn cờ.';
    $('auth-submit').textContent = register ? 'Đăng ký' : 'Đăng nhập';
    $('auth-toggle').textContent = register ? 'Đã có tài khoản? Đăng nhập' : 'Chưa có tài khoản? Đăng ký';
    $('auth-password').autocomplete = register ? 'new-password' : 'current-password';
}
async function handleError(error) {
    if (error.status === 401 && user) {
        await connection?.stop(); connection = null; authScreen();
        notice('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.', true);
    } else notice(error.message, true);
}
function bind(id, event, handler) {
    $(id).addEventListener(event, async e => {
        e.preventDefault();
        if ($(id).dataset.working) return;
        $(id).dataset.working = 'true';
        try { await handler(e); } catch (error) { await handleError(error); }
        finally { delete $(id).dataset.working; }
    });
}
async function initializeUser() {
    user = await api('/api/auth/me'); $('username').textContent = user.username; $('identity').hidden = false;
    await connect(); await route();
}
async function route() {
    const match = location.pathname.match(/^\/rooms\/(join\/)?([A-Za-z0-9]{8})$/);
    if (match && !match[1]) {
        try { await openRoom(match[2], false); return; }
        catch (error) { if (![403, 404].includes(error.status)) throw error; }
    }
    await lobby();
    if (match) { $('join-code').value = match[2].toUpperCase(); $('join-confirmation').hidden = false; }
}
function navigate(path) { history.pushState({}, '', path); notice(''); }
async function lobby() {
    if (room && connection?.state === signalR.HubConnectionState.Connected) await connection.invoke('Unsubscribe', room.code);
    room = null; show('lobby'); $('join-confirmation').hidden = true;
    await Promise.all([loadRooms(), loadInvites()]);
}
function text(tag, value, className) { const node = document.createElement(tag); node.textContent = value; if (className) node.className = className; return node; }
function action(label, fn, className = 'quiet') {
    const button = text('button', label, className); button.type = 'button';
    button.onclick = async () => { button.disabled = true; try { await fn(); } catch (e) { await handleError(e); } finally { button.disabled = false; } };
    return button;
}
async function loadRooms() {
    const rooms = await api('/api/rooms'); const list = $('room-list'); list.replaceChildren();
    if (!rooms.length) list.append(text('p', 'Chưa có phòng nào. Tạo phòng để bắt đầu.', 'empty'));
    for (const item of rooms) {
        const row = text('div', '', 'room-row'); const details = text('div', '');
        details.append(text('strong', item.code), text('p', `${item.players.map(p => p.username).join(' · ')} — ${item.status === 'waiting' ? 'Đang chờ bạn' : item.status === 'finished' ? 'Đã kết thúc' : 'Đang chơi'}`, 'muted'));
        row.append(details, action('Tiếp tục →', () => openRoom(item.code))); list.append(row);
    }
}
async function loadInvites() {
    const invitations = await api('/api/invitations'); const list = $('invitations'); list.replaceChildren();
    if (!invitations.length) list.append(text('p', 'Bạn chưa có lời mời mới.', 'empty'));
    for (const item of invitations) {
        const row = text('div', '', 'invitation-row'); const details = text('div', '');
        details.append(text('strong', item.hostUsername), text('p', `Mời bạn vào phòng ${item.code}`, 'muted'));
        const controls = text('div', '', 'row-actions');
        controls.append(action('Chấp nhận', async () => { const state = await api(`/api/invitations/${item.id}/accept`, { method: 'POST' }); await openRoom(state.code); }, 'primary'),
            action('Từ chối', async () => { await api(`/api/invitations/${item.id}/decline`, { method: 'POST' }); await loadInvites(); }));
        row.append(details, controls); list.append(row);
    }
}
function applyState(state) {
    if (!room || room.code !== state.code || state.revision < room.revision) return;
    room = state; renderRoom();
}
async function openRoom(code, push = true) {
    const state = await api(`/api/rooms/${encodeURIComponent(code)}`);
    if (room && room.code !== state.code && connection?.state === signalR.HubConnectionState.Connected) await connection.invoke('Unsubscribe', room.code);
    room = state; board.selected = null; if (push) navigate(`/rooms/${state.code}`); show('game'); renderRoom();
    if (connection?.state === signalR.HubConnectionState.Connected) applyState(await connection.invoke('Subscribe', state.code));
}
function renderRoom() {
    if (!room || !user) return;
    $('room-code').textContent = room.code;
    $('red-name').textContent = room.players.find(p => p.color === 'do')?.username;
    $('black-name').textContent = room.players.find(p => p.color === 'den')?.username || 'Đang chờ người chơi…';
    const own = room.players.find(p => p.id === user.id)?.color;
    $('your-color').textContent = `Bạn cầm quân ${own === 'do' ? 'đỏ' : 'đen'}.`;
    $('turn-label').textContent = room.status === 'waiting' ? 'Đang chờ người bạn…' : room.status === 'finished'
        ? `Quân ${room.board.winner === 'do' ? 'đỏ' : 'đen'} thắng!` : `Lượt quân ${room.board.turn === 'do' ? 'đỏ' : 'đen'}${own === room.board.turn ? ' · Đến lượt bạn' : ''}`;
    $('save-status').textContent = busy ? 'Đang lưu nước đi…' : `Đã lưu · ${new Date(room.updatedAt).toLocaleString('vi-VN')}`;
    $('invite-panel').hidden = room.hostId !== user.id || room.players.length === 2;
    board.render(room, user.id, busy);
}
async function move(pieceId, row, col) {
    if (busy || !room) return;
    busy = true; renderRoom(); const code = room.code;
    try { applyState(await api(`/api/rooms/${code}/moves`, { method: 'POST', body: { pieceId, row, col, expectedRevision: room.revision } })); board.selected = null; notice(''); }
    catch (e) {
        // A lost response may still represent a committed move: always reload instead of retrying the move.
        try { applyState(await api(`/api/rooms/${code}`)); } catch { /* The original error explains the failure. */ }
        await handleError(e);
    } finally { busy = false; renderRoom(); }
}
function connectionStatus(message) { $('connection-status').textContent = message; }
async function connect() {
    if (connection) return;
    connection = new signalR.HubConnectionBuilder().withUrl('/hubs/game').withAutomaticReconnect([0, 2000, 5000, 10000]).build();
    connection.on('RoomUpdated', applyState);
    connection.onreconnecting(() => connectionStatus('Đang kết nối lại…'));
    connection.onreconnected(async () => {
        connectionStatus('Đã kết nối trực tuyến');
        if (room) try { applyState(await connection.invoke('Subscribe', room.code)); } catch (e) { await handleError(e); }
    });
    connection.onclose(() => connectionStatus('Mất kết nối trực tiếp · dùng Tải lại bàn cờ'));
    try { await connection.start(); connectionStatus('Đã kết nối trực tuyến'); }
    catch { connectionStatus('Chưa kết nối trực tiếp · đang đồng bộ định kỳ'); }
}
bind('auth-toggle', 'click', () => { register = !register; if (location.pathname.startsWith('/account')) navigate(register ? '/account/register' : '/account/login'); authScreen(); });
bind('auth-form', 'submit', async () => {
    $('auth-submit').disabled = true;
    try { await api(`/api/auth/${register ? 'register' : 'login'}`, { method: 'POST', body: { username: $('auth-username').value.trim(), password: $('auth-password').value } });
        $('auth-password').value = ''; resetCsrf(); notice(''); await initializeUser();
    } finally { $('auth-submit').disabled = false; }
});
bind('logout', 'click', async () => {
    await api('/api/auth/logout', { method: 'POST' }); await connection?.stop(); connection = null; resetCsrf(); navigate('/'); authScreen();
});
bind('create-room', 'click', async () => { const state = await api('/api/rooms', { method: 'POST' }); await openRoom(state.code); });
bind('join-form', 'submit', async () => { const code = $('join-code').value.trim().toUpperCase(); const state = await api(`/api/rooms/${encodeURIComponent(code)}/join`, { method: 'POST' }); await openRoom(state.code); });
bind('back-lobby', 'click', async () => { navigate('/'); await lobby(); });
bind('refresh-invites', 'click', loadInvites);
bind('reload-board', 'click', async () => { if (room) { applyState(await api(`/api/rooms/${room.code}`)); if (connection?.state === signalR.HubConnectionState.Disconnected) { await connection.start(); applyState(await connection.invoke('Subscribe', room.code)); connectionStatus('Đã kết nối trực tuyến'); } } });
bind('invite-form', 'submit', async () => { await api(`/api/rooms/${room.code}/invitations`, { method: 'POST', body: { username: $('invite-username').value.trim() } }); $('invite-username').value = ''; notice('Đã gửi lời mời.'); });
async function copy(value) { try { await navigator.clipboard.writeText(value); notice('Đã sao chép.'); } catch { notice(`Sao chép: ${value}`); } }
bind('copy-code', 'click', () => copy(room.code));
bind('copy-link', 'click', () => copy(`${location.origin}/rooms/join/${room.code}`));
window.addEventListener('popstate', () => { if (user) route().catch(handleError); });
// Recover missed notifications and refresh the invitation inbox without a second realtime subsystem.
setInterval(async () => {
    if (!user || document.hidden || busy) return;
    try { if (room) applyState(await api(`/api/rooms/${room.code}`)); else await Promise.all([loadRooms(), loadInvites()]); }
    catch (e) { await handleError(e); }
}, 15000);
try { await initializeUser(); } catch (e) { authScreen(); if (e.status !== 401) notice(e.message, true); }
