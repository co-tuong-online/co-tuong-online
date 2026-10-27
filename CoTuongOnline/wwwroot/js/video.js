// video.js — gọi video 1-1 trong phòng bằng WebRTC. Server chỉ chuyển tín hiệu (SignalR), hình/tiếng đi P2P qua STUN.
// Luồng: A CallRequest → B CALL_REQUEST → B CallAccept → A CALL_ACCEPT → A tạo OFFER → B trả ANSWER → hai bên trao ICE.
// Lưu ý: camera/micro chỉ dùng được trên HTTPS (hoặc localhost). Không có TURN nên demo trong cùng mạng.

const VideoCall = {
  ICE_SERVERS: [{ urls: 'stun:stun.l.google.com:19302' }, { urls: 'stun:stun1.l.google.com:19302' }],

  code: null,
  state: 'idle',        // idle | calling | ringing | connecting | active
  opponent: null,
  pc: null,
  localStream: null,
  pendingIce: [],
  ringModal: null,
  callTimer: null,

  init(code) {
    this.code = code;
    $('#callBtn').addEventListener('click', () => this.call());
    $('#hangupBtn').addEventListener('click', () => this.hangup(true));
    $('#micBtn').addEventListener('click', () => this.toggleTrack('audio'));
    $('#camBtn').addEventListener('click', () => this.toggleTrack('video'));

    Realtime.on('CALL_REQUEST', p => this.onRequest(p));
    Realtime.on('CALL_ACCEPT', () => this.onAccepted());
    Realtime.on('CALL_REJECT', () => { UI.toast('Đối thủ đã từ chối cuộc gọi.'); this.cleanup(); });
    Realtime.on('CALL_END', () => { if (this.state !== 'idle') UI.toast('Cuộc gọi đã kết thúc.'); this.cleanup(); });
    Realtime.on('OFFER', p => this.onOffer(p.sdp));
    Realtime.on('ANSWER', p => this.onAnswer(p.sdp));
    Realtime.on('ICE_CANDIDATE', p => this.onIce(p.candidate));
    this.render();
  },

  setOpponent(player) {
    this.opponent = player || null;
    this.render();
  },

  peerLeft() { if (this.state !== 'idle') this.cleanup(); },

  // ------------------------------------------------------------ gọi đi

  async call() {
    if (!this.opponent) return UI.toast('Phòng chưa có đối thủ để gọi.');
    if (!this.checkSupport()) return;
    try {
      await Realtime.invoke('CallRequest', this.code);
      this.setState('calling');
      // Không ai nghe máy sau 30 giây thì huỷ
      this.callTimer = setTimeout(() => {
        if (this.state === 'calling') { UI.toast('Đối thủ không trả lời.'); this.hangup(true); }
      }, 30000);
    } catch (e) { UI.error(e); }
  },

  async onAccepted() {
    if (this.state !== 'calling') return;
    clearTimeout(this.callTimer);
    this.setState('connecting');
    if (!await this.startMedia()) return this.hangup(true);
    this.createPeer();
    const offer = await this.pc.createOffer();
    await this.pc.setLocalDescription(offer);
    await Realtime.invoke('SendOffer', this.code, JSON.stringify(this.pc.localDescription));
  },

  async onAnswer(sdp) {
    if (!this.pc) return;
    await this.pc.setRemoteDescription(JSON.parse(sdp));
    await this.flushIce();
  },

  // ------------------------------------------------------------ nhận cuộc gọi

  async onRequest(p) {
    if (this.state !== 'idle') { // đang bận → từ chối
      Realtime.invoke('CallReject', this.code).catch(() => {});
      return;
    }
    this.setState('ringing');
    Sound.play('notify');
    const accept = await UI.modal({
      title: 'Cuộc gọi video',
      html: `<p><b>${UI.escape(p.fromNickname)}</b> muốn gọi video với bạn.</p>`,
      buttons: [{ label: 'Từ chối', value: false, kind: 'ghost' }, { label: 'Nghe máy', value: true, kind: 'primary' }],
      onOpen: (m, close) => { this.ringModal = close; setTimeout(() => close(false), 30000); }
    });
    this.ringModal = null;
    if (this.state !== 'ringing') return; // người gọi đã huỷ
    if (!accept || !this.checkSupport()) {
      Realtime.invoke('CallReject', this.code).catch(() => {});
      return this.cleanup();
    }
    this.setState('connecting');
    if (!await this.startMedia()) {
      Realtime.invoke('CallReject', this.code).catch(() => {});
      return this.cleanup();
    }
    this.createPeer();
    await Realtime.invoke('CallAccept', this.code);
  },

  async onOffer(sdp) {
    if (!this.pc) return;
    await this.pc.setRemoteDescription(JSON.parse(sdp));
    await this.flushIce();
    const answer = await this.pc.createAnswer();
    await this.pc.setLocalDescription(answer);
    await Realtime.invoke('SendAnswer', this.code, JSON.stringify(this.pc.localDescription));
  },

  // ------------------------------------------------------------ WebRTC

  checkSupport() {
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      UI.toast('Gọi video cần chạy qua HTTPS (hoặc localhost). Hãy mở trang bằng địa chỉ https://', 'error', 6000);
      return false;
    }
    return true;
  },

  async startMedia() {
    try {
      this.localStream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 }, audio: true });
    } catch {
      try {
        this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        UI.toast('Không mở được camera, chỉ gọi thoại.');
      } catch {
        UI.toast('Không truy cập được camera/micro. Hãy cho phép trong trình duyệt.', 'error', 6000);
        return false;
      }
    }
    $('#localVideo').srcObject = this.localStream;
    $('#localWrap').classList.remove('hidden');
    return true;
  },

  createPeer() {
    this.pc = new RTCPeerConnection({ iceServers: this.ICE_SERVERS });
    this.localStream.getTracks().forEach(t => this.pc.addTrack(t, this.localStream));
    this.pc.onicecandidate = e => {
      if (e.candidate) Realtime.invoke('SendIceCandidate', this.code, e.candidate.toJSON()).catch(() => {});
    };
    this.pc.ontrack = e => {
      $('#remoteVideo').srcObject = e.streams[0];
      this.setState('active');
    };
    this.pc.onconnectionstatechange = () => {
      const s = this.pc?.connectionState;
      if (s === 'failed') { UI.toast('Không kết nối được video (khác mạng, cần máy chủ TURN).', 'error'); this.hangup(true); }
    };
  },

  async onIce(candidate) {
    if (!this.pc || !this.pc.remoteDescription) { this.pendingIce.push(candidate); return; }
    try { await this.pc.addIceCandidate(candidate); } catch { /* bỏ qua ứng viên lỗi */ }
  },

  async flushIce() {
    const list = this.pendingIce.splice(0);
    for (const c of list) { try { await this.pc.addIceCandidate(c); } catch { /* bỏ qua */ } }
  },

  toggleTrack(kind) {
    const track = this.localStream?.getTracks().find(t => t.kind === kind);
    if (!track) return;
    track.enabled = !track.enabled;
    this.render();
  },

  // ------------------------------------------------------------ kết thúc

  hangup(notify) {
    if (this.state === 'idle') return;
    if (notify) Realtime.invoke('CallEnd', this.code).catch(() => {});
    this.cleanup();
  },

  cleanup() {
    clearTimeout(this.callTimer);
    this.ringModal?.(false);
    this.ringModal = null;
    this.pc?.close();
    this.pc = null;
    this.localStream?.getTracks().forEach(t => t.stop());
    this.localStream = null;
    this.pendingIce = [];
    $('#remoteVideo').srcObject = null;
    $('#localVideo').srcObject = null;
    $('#localWrap').classList.add('hidden');
    this.setState('idle');
  },

  setState(s) {
    this.state = s;
    this.render();
  },

  render() {
    const s = this.state;
    const labels = { idle: 'Chưa gọi', calling: 'Đang gọi…', ringing: 'Có cuộc gọi', connecting: 'Đang kết nối…', active: 'Đang gọi' };
    const badge = $('#callState');
    if (!badge) return;
    badge.textContent = labels[s];
    badge.className = 'badge ' + (s === 'active' ? 'ok' : s === 'idle' ? '' : 'warn');
    $('#callBtn').classList.toggle('hidden', s !== 'idle');
    $('#callBtn').disabled = !this.opponent;
    $('#hangupBtn').classList.toggle('hidden', s === 'idle' || s === 'ringing');
    const inCall = s === 'active' || s === 'connecting';
    const audio = this.localStream?.getAudioTracks()[0];
    const video = this.localStream?.getVideoTracks()[0];
    $('#micBtn').classList.toggle('hidden', !inCall || !audio);
    $('#camBtn').classList.toggle('hidden', !inCall || !video);
    if (audio) $('#micBtn').textContent = audio.enabled ? 'Tắt mic' : 'Bật mic';
    if (video) $('#camBtn').textContent = video.enabled ? 'Tắt camera' : 'Bật camera';
    const ph = $('#videoPlaceholder');
    ph.classList.toggle('hidden', s === 'active');
    ph.textContent = {
      idle: this.opponent ? `Gọi cho ${this.opponent.nickname} để thấy nhau trong lúc đánh.` : 'Khi có đối thủ vào phòng, bạn có thể gọi video.',
      calling: 'Đang chờ đối thủ nghe máy…',
      ringing: 'Có cuộc gọi đến…',
      connecting: 'Đang kết nối hình ảnh…',
      active: ''
    }[s];
  }
};
