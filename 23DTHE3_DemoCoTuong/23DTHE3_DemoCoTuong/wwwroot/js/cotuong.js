const { createApp } = Vue;

createApp({

    data() {
        return {

            rows: 10,
            cols: 9,

            pieces: [],

            selectedPiece: null,
            connection: null,
            roomId: "room001",

            // =====================
            // CHAT
            // =====================

            userName: "Player 1",

            chatMessage: "",

            messages: [],
            peerConnection: null,

            localStream: null,

            pendingIceCandidates: [],

            grid: {
                left: 15.5,
                right: 84.5,
                top: 10.5,
                bottom: 91
            }
        };
    },


    async  mounted() {

        await this.loadBoard();
        await this.connectSignalR();

    },


    methods: {
        async flushIceCandidates() {

            if (
                !this.peerConnection ||
                !this.peerConnection.remoteDescription
            ) {
                return;
            }


            while (
                this.pendingIceCandidates.length > 0
            ) {

                const candidate =
                    this.pendingIceCandidates.shift();


                try {

                    await this.peerConnection
                        .addIceCandidate(
                            candidate
                        );

                }
                catch (error) {

                    console.error(
                        "Pending ICE error:",
                        error
                    );

                }

            }

        },
        async startCall() {

            try {

                await this.openCamera();


                this.createPeerConnection();


                const offer =
                    await this.peerConnection
                        .createOffer();


                await this.peerConnection
                    .setLocalDescription(
                        offer
                    );


                await this.connection.invoke(

                    "SendOffer",

                    this.roomId,

                    JSON.stringify(offer)

                );


                console.log(
                    "Đã gửi WebRTC Offer"
                );

            }
            catch (error) {

                console.error(
                    "Call error:",
                    error
                );

            }

        },
        createPeerConnection() {

            if (this.peerConnection) {
                return;
            }


            this.peerConnection =
                new RTCPeerConnection({

                    iceServers: [
                        {
                            urls:
                                "stun:stun.l.google.com:19302"
                        }
                    ]

                });


            // ==================================
            // Gửi camera + microphone của mình
            // ==================================

            this.localStream
                .getTracks()
                .forEach(track => {

                    this.peerConnection
                        .addTrack(
                            track,
                            this.localStream
                        );

                });


            // ==================================
            // Nhận video người kia
            // ==================================

            this.peerConnection.ontrack =
                (event) => {

                    this.$refs.remoteVideo.srcObject =
                        event.streams[0];

                };


            // ==================================
            // Gửi ICE candidate qua SignalR
            // ==================================

            this.peerConnection.onicecandidate =
                async (event) => {

                    if (!event.candidate) {
                        return;
                    }


                    await this.connection.invoke(

                        "SendIceCandidate",

                        this.roomId,

                        JSON.stringify(
                            event.candidate
                        )

                    );

                };


            // Debug
            this.peerConnection.onconnectionstatechange =
                () => {

                    console.log(
                        "WebRTC:",
                        this.peerConnection.connectionState
                    );

                };

        },
        async openCamera() {

            if (this.localStream) {
                return;
            }


            this.localStream =
                await navigator.mediaDevices
                    .getUserMedia({
                        video: true,
                        audio: true
                    });


            this.$refs.localVideo.srcObject =
                this.localStream;
        },
        async sendMessage() {

            const message =
                this.chatMessage.trim();


            if (message === "")
                return;


            if (!this.connection)
                return;


            await this.connection.invoke(
                "SendMessage",

                this.roomId,
                this.userName,
                message
            );


            // Xóa textbox
            this.chatMessage = "";

        },
        applyMove(move) {

            const piece =
                this.pieces.find(
                    p => p.id === move.pieceId
                );

            if (!piece) {
                console.error(
                    "Không tìm thấy quân:",
                    move.pieceId
                );

                return;
            }


            // Kiểm tra quân tại vị trí đích
            const capturedPiece =
                this.pieces.find(
                    p =>
                        p.row === move.toRow &&
                        p.col === move.toCol &&
                        p.id !== move.pieceId
                );


            // Nếu có -> loại khỏi bàn
            if (capturedPiece) {

                this.pieces =
                    this.pieces.filter(
                        p => p.id !== capturedPiece.id
                    );

            }


            // Di chuyển
            piece.row = move.toRow;
            piece.col = move.toCol;
        },
        async connectSignalR() {

            this.connection =
                new signalR.HubConnectionBuilder()
                    .withUrl("/chessHub")
                    .withAutomaticReconnect()
                    .build();

            this.connection.on(
                "ReceiveOffer",

                async (offerJson) => {

                    try {

                        console.log(
                            "Nhận Offer"
                        );


                        // Mở camera người nhận
                        await this.openCamera();


                        // Tạo WebRTC connection
                        this.createPeerConnection();


                        const offer =
                            JSON.parse(
                                offerJson
                            );


                        // Nhận Offer
                        await this.peerConnection
                            .setRemoteDescription(
                                offer
                            );


                        // Xử lý ICE đến sớm
                        await this.flushIceCandidates();


                        // Tạo Answer
                        const answer =
                            await this.peerConnection
                                .createAnswer();


                        await this.peerConnection
                            .setLocalDescription(
                                answer
                            );


                        // Gửi Answer về người gọi
                        await this.connection.invoke(

                            "SendAnswer",

                            this.roomId,

                            JSON.stringify(answer)

                        );

                    }
                    catch (error) {

                        console.error(
                            "ReceiveOffer error:",
                            error
                        );

                    }

                }
            );

            this.connection.on(
                "ReceiveAnswer",

                async (answerJson) => {

                    try {

                        const answer =
                            JSON.parse(
                                answerJson
                            );


                        await this.peerConnection
                            .setRemoteDescription(
                                answer
                            );


                        // Add các ICE đã đến trước Answer
                        await this.flushIceCandidates();


                        console.log(
                            "Đã nhận Answer"
                        );

                    }
                    catch (error) {

                        console.error(
                            "Answer error:",
                            error
                        );

                    }

                }
            );
            this.connection.on(
                "ReceiveIceCandidate",

                async (candidateJson) => {

                    try {

                        const candidate =
                            new RTCIceCandidate(
                                JSON.parse(candidateJson)
                            );


                        // Chưa có PeerConnection
                        // hoặc chưa setRemoteDescription
                        if (
                            !this.peerConnection ||
                            !this.peerConnection.remoteDescription
                        ) {

                            this.pendingIceCandidates.push(
                                candidate
                            );

                            return;
                        }


                        await this.peerConnection
                            .addIceCandidate(
                                candidate
                            );

                    }
                    catch (error) {

                        console.error(
                            "ICE error:",
                            error
                        );

                    }

                }
            );
            // =========================
            // Nhận nước đi
            // =========================

            this.connection.on(
                "ReceiveMove",
                (move) => {

                    console.log(
                        "ReceiveMove:",
                        move
                    );

                    this.applyMove(move);

                }
            );

            this.connection.on(
                "ReceiveMessage",
                (userName, message) => {

                    console.log(
                        "Chat:",
                        userName,
                        message
                    );

                    this.messages.push({
                        userName: userName,
                        message: message
                    });


                    // Tự động kéo xuống tin nhắn cuối
                    this.$nextTick(() => {

                        const chat =
                            this.$refs.chatMessages;

                        if (chat) {
                            chat.scrollTop =
                                chat.scrollHeight;
                        }

                    });

                }
            );


            // =========================
            // Connect
            // =========================

            await this.connection.start();

            console.log("SignalR connected");


            // =========================
            // Join room
            // =========================

           
        },

        // ==========================
        // LOAD DATA
        // ==========================

        async loadBoard() {

            try {

                const response =
                    await fetch("/api/CoTuong/board");

                if (!response.ok) {
                    throw new Error(
                        "Không lấy được dữ liệu bàn cờ"
                    );
                }

                const sp = await response.json();
                var data = JSON.parse(sp.data);
                this.pieces = data.pieces;

            }
            catch (error) {

                console.error(
                    "Load board error:",
                    error
                );

            }
        },


        // ==========================
        // TÍNH VỊ TRÍ QUÂN CỜ
        // ==========================

        getPieceStyle(piece) {

            const width =
                this.grid.right -
                this.grid.left;

            const height =
                this.grid.bottom -
                this.grid.top;


            const x =
                this.grid.left +
                piece.col *
                width /
                (this.cols - 1);


            const y =
                this.grid.top +
                piece.row *
                height /
                (this.rows - 1);


            return {

                left: x + "%",

                top: y + "%"

            };
        },


        // ==========================
        // CHỌN QUÂN
        // ==========================

        selectPiece(piece) {

            this.selectedPiece =
                piece;

            console.log(
                "Selected:",
                piece
            );

        },


        // ==========================
        // CLICK BÀN CỜ
        // ==========================

        moveTo(event) {

            if (!this.selectedPiece)
                return;


            const board =
                this.$refs.board;


            const rect =
                board.getBoundingClientRect();


            /*
                Tọa độ pixel click
                bên trong bàn cờ
            */

            const mouseX =
                event.clientX -
                rect.left;

            const mouseY =
                event.clientY -
                rect.top;


            /*
                Chuyển sang %
            */

            const percentX =
                mouseX /
                rect.width *
                100;


            const percentY =
                mouseY /
                rect.height *
                100;


            /*
                Chuyển tọa độ %
                sang row / col
            */

            let col =
                Math.round(

                    (percentX -
                        this.grid.left)

                    /

                    (this.grid.right -
                        this.grid.left)

                    *

                    (this.cols - 1)

                );


            let row =
                Math.round(

                    (percentY -
                        this.grid.top)

                    /

                    (this.grid.bottom -
                        this.grid.top)

                    *

                    (this.rows - 1)

                );


            /*
                Không cho vượt bàn cờ
            */

            col =
                Math.max(
                    0,
                    Math.min(
                        this.cols - 1,
                        col
                    )
                );


            row =
                Math.max(
                    0,
                    Math.min(
                        this.rows - 1,
                        row
                    )
                );


            this.movePiece(
                this.selectedPiece,
                row,
                col
            );

        },


        // ==========================
        // DI CHUYỂN
        // ==========================

        async movePiece(piece, row, col) {

            const fromRow = piece.row;
            const fromCol = piece.col;


            await this.connection.invoke(
                "MovePiece",

                this.roomId,

                piece.id,

                fromRow,
                fromCol,

                row,
                col
            );


            this.selectedPiece = null;
        }

    }

}).mount("#app");