# Cờ Tướng Online · Nhóm SOS (23DTHE3)

Đồ án môn Quản lý dự án CNTT. Backend ASP.NET Core 8 Web API + SignalR, frontend HTML/CSS/JS thuần trong `wwwroot`.

Đủ 7 chức năng theo phạm vi đã chốt:

1. Đăng ký, đăng nhập, quên mật khẩu, có OTP email.
2. Tạo phòng mã 6 số. Mời bạn bằng mã, bằng link hoặc theo biệt danh.
3. Chơi online 2 người. Server kiểm tra luật và tự xử hết giờ / mất kết nối.
4. Chat trong phòng.
5. Gọi video (WebRTC + STUN).
6. Chơi với máy, mức Dễ và Khó.
7. Lưu và tải ván đánh với máy.

> **Dữ liệu lưu trong SQL Server** (EF Core). Lần chạy đầu, server tự tạo database `CoTuongOnline` và các bảng, kèm 4 tài khoản demo. Không cần chạy lệnh SQL nào. Chi tiết ở mục 5.

---

## 1. Chạy thử

Yêu cầu:
- .NET SDK 8. File `global.json` đã khoá SDK 8.
- SQL Server LocalDB, có sẵn khi cài Visual Studio (kiểm tra bằng lệnh `sqllocaldb info`). Máy không có thì xem mục 5.

```bash
cd CoTuongOnline
dotnet restore
dotnet dev-certs https --trust      # chỉ cần 1 lần; để trình duyệt cho dùng camera khi gọi video
dotnet run --launch-profile https
```

Mở **https://localhost:7180**. Với Visual Studio: mở `CoTuongOnline.csproj`, chọn profile `https`, bấm F5.

Tài khoản demo cho anh em test nhanh (mật khẩu `demo1234`): `khoi_pham`, `minh_ng`, `dang_hd`, `quan_td`. Trang đăng nhập không hiện danh sách này, người ngoài phải đăng ký thật.

**Thử chơi 2 người trên 1 máy:**
- Mở 1 cửa sổ thường và 1 cửa sổ ẩn danh, đăng nhập 2 tài khoản khác nhau.
- Mỗi cửa sổ giữ phiên đăng nhập riêng.

**Đăng ký tài khoản mới:**
- Ở môi trường Development, server **không gửi email thật**.
- Mã OTP hiện ngay trên form (dòng "Chế độ dev…") và in trong console của server.

**Swagger** cho Tester: https://localhost:7180/swagger
1. Gọi `POST /api/v1/auth/login` để lấy token.
2. Bấm **Authorize** và dán token vào.

### Demo qua mạng LAN (2 máy)

```bash
dotnet run --launch-profile lan
```

Máy còn lại mở `https://<IP-máy-chạy-server>:7180`:
- Trình duyệt sẽ cảnh báo chứng chỉ, chọn "Tiếp tục".
- Windows hỏi tường lửa thì cho phép ở mạng Private.

Video call chỉ dùng STUN (không có TURN), nên 2 máy phải chung mạng.

### Gửi OTP qua Gmail thật

Trong `appsettings.json` đặt:
- `Email:DevMode = false`
- `Email:FromAddress`
- `Email:Smtp:Username`

App Password **không ghi vào file**, đặt bằng biến môi trường:

```powershell
setx Email__Smtp__Password "xxxx xxxx xxxx xxxx"
setx Jwt__Key "mot-chuoi-bi-mat-dai-it-nhat-32-ky-tu"
```

---

## 2. Cấu trúc thư mục và người phụ trách

| Thư mục / file | Nội dung | Phụ trách |
|---|---|---|
| `Engine/` | Luật cờ tướng, sinh/kiểm tra nước đi, ký hiệu "Pháo 2 bình 5", AI Dễ/Khó | Quân (BE2) |
| `Services/Auth/`, `Services/Email/`, `Controllers/AuthController.cs` | Đăng ký, OTP, JWT, BCrypt, MailKit | Đăng (BE1) |
| `Services/Rooms/RoomService.cs`, `Services/Games/SavedGameService.cs` | Phòng chơi, lưu/tải ván | Đăng (BE1) |
| `Services/Games/OnlineGameService.cs`, `AiGameService.cs` | Ván online (đồng hồ, kết thúc), ván với máy | Quân (BE2) |
| `Hubs/GameHub.cs`, `Services/Realtime/`, `Services/Rooms/InviteService.cs`, `GameMaintenanceService.cs` | SignalR, lời mời, chat, signaling video, mất kết nối | Triết (BE3) |
| `wwwroot/js/board.js`, `ai-game.html`, `js/pages/ai-game.js` | Bàn cờ, kéo-thả, gameplay | Tính (FE1) |
| `login/register/forgot-password/lobby/room/saved-games.html`, `js/video.js` | Giao diện tài khoản, phòng, chat, video | Minh (FE2) |
| `Data/`, `Repositories/`, `Migrations/` | Tầng dữ liệu: EF Core + SQL Server (và bản RAM dự phòng) | Đăng (BE1) |

Kiến trúc: **Controller → Service → Repository**. Controller chỉ nhận request và trả `{ success, code, message, data }`; mọi nghiệp vụ nằm ở Service.

### Toạ độ bàn cờ

`board[row][col]` gồm 10 hàng × 9 cột:
- **row 0 là phía Đen (trên), row 9 là phía Đỏ (dưới)**, theo `co_tuong_initial.json` của thầy.
- Mã quân gồm màu + loại: `r`/`b` + `K A B N R C P` (Tướng, Sĩ, Tượng, Mã, Xe, Pháo, Tốt).
- Nước đi gửi lên dạng `{ "from": { "row": 7, "col": 7 }, "to": { "row": 7, "col": 4 } }`.
- Người cầm Đen thấy bàn bị đảo, nhưng **chỉ là đảo cách hiển thị** (`board.js`), dữ liệu không đổi.

---

## 3. API REST (`/api/v1`)

| Method | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/auth/register` | Đăng ký, gửi OTP |
| POST | `/auth/verify-otp` | Xác nhận OTP → trả JWT |
| POST | `/auth/resend-otp` | Gửi lại OTP (`purpose`: `REGISTER` / `RESET_PASSWORD`) |
| POST | `/auth/login` | Đăng nhập bằng email hoặc biệt danh |
| POST | `/auth/forgot-password`, `/auth/reset-password` | Quên mật khẩu |
| GET | `/auth/me` | Thông tin + thành tích |
| GET | `/users/search?q=`, `/users/online` | Tìm người để mời, danh sách online |
| GET/POST | `/rooms` | Danh sách phòng chờ / tạo phòng |
| GET | `/rooms/current`, `/rooms/{code}` | Phòng đang ở / chi tiết phòng |
| POST | `/rooms/{code}/join`, `/leave`, `/ready`, `/start`, `/kick` | Thao tác phòng |
| PUT | `/rooms/{code}/config` | Đổi cấu hình (chủ phòng) |
| GET | `/rooms/{code}/messages` | Lịch sử chat |
| GET | `/games/{id}/state` | Trạng thái ván online (khi tải lại trang / kết nối lại) |
| POST | `/ai-games`, `/ai-games/{id}/moves`, `/ai-move`, `/undo`, `/resign` | Ván với máy |
| GET/POST/PATCH/DELETE | `/saved-games`, `/saved-games/{id}`, `/saved-games/{id}/resume` | Ván đã lưu |

Mã lỗi nằm trong `Common/ApiResponse.cs` (`ErrorCodes`).

## 4. SignalR (`/hubs/game`)

Mỗi phòng là Group `room:{code}`.

| Client gọi (`invoke`) | Server phát (`on`) |
|---|---|
| `JoinRoom(code)`, `LeaveRoom(code)` | `ROOM_UPDATED`, `PLAYER_JOINED`, `PLAYER_LEFT`, `KICKED` |
| `SendInvite(code, toUserId, toNickname)`, `RespondInvite(inviteId, accept)` | `INVITE_RECEIVED`, `INVITE_UPDATED` |
| `MakeMove(gameId, from, to, seq)`, `Resign(gameId)` | `GAME_STARTED`, `MOVE_ACCEPTED`, `MOVE_REJECTED`, `GAME_ENDED`, `PLAYER_OFFLINE`, `PLAYER_ONLINE` |
| `SendChat(code, content)` | `CHAT_MESSAGE` |
| `CallRequest/Accept/Reject/End(code)`, `SendOffer/SendAnswer(code, sdp)`, `SendIceCandidate(code, candidate)` | `CALL_REQUEST`, `CALL_ACCEPT`, `CALL_REJECT`, `CALL_END`, `OFFER`, `ANSWER`, `ICE_CANDIDATE` |

**Mất kết nối:**
- Sau 15 giây, server phát `PLAYER_OFFLINE`.
- Thêm 60 giây mà người đó chưa quay lại thì bị xử thua (`GAME_ENDED`, lý do `DISCONNECT`).
- Client tự kết nối lại và gọi lại `JoinRoom`.

**Hết giờ:** server tự phát hiện bằng `GameMaintenanceService`, không chờ client báo.

---

## 5. Cơ sở dữ liệu

**Git chỉ lưu code và cấu trúc bảng (thư mục `Migrations`), không lưu dữ liệu.** Mỗi người clone về và chạy sẽ có database riêng trên máy mình. Tài khoản đăng ký ở máy này không có ở máy khác, và đó là điều bình thường khi phát triển.

Muốn nhiều người dùng chung dữ liệu (buổi demo), cho **1 máy chạy server** bằng `dotnet run --launch-profile lan`, các máy khác mở trình duyệt vào máy đó.

### Cấu hình

Trong `appsettings.json`:

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "Default": "Server=(localdb)\\MSSQLLocalDB;Database=CoTuongOnline;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

| Máy bạn có | Sửa `Server=` thành |
|---|---|
| LocalDB (cài kèm Visual Studio) | `(localdb)\\MSSQLLocalDB` (mặc định) |
| SQL Server Express | `.\\SQLEXPRESS` |
| Không có SQL Server | Đổi `"Provider"` thành `"InMemory"` → lưu RAM, tắt server là mất |

Muốn đổi riêng trên máy mình mà không ảnh hưởng người khác: đặt biến môi trường `ConnectionStrings__Default`, không sửa file rồi commit.

### Bảng

`users`, `otp_codes`, `games`, `game_moves`, `saved_games`. Cấu hình ở `Data/AppDbContext.cs`, entity ở `Models/Entities.cs`.
- Cột trạng thái ván (`CurrentStateJson`, `StateJson`) có ràng buộc `ISJSON`.
- Mỗi nước đi và trạng thái ván được ghi trong **cùng một transaction** (`EfGameRepository.RecordMoveAsync`).

Phòng chơi, ván đang đánh và danh sách online vẫn giữ trong RAM vì thay đổi liên tục. Khi server khởi động lại:

| Dữ liệu | Sau khi khởi động lại |
|---|---|
| Tài khoản, ván đã lưu, lịch sử ván và nước đi | Còn nguyên |
| Ván **với máy** đang dở | Chơi tiếp được: mở lại `ai-game.html?id=...`, server nạp lại thế cờ từ `games.CurrentStateJson`. Riêng lượt "đi lại" tính lại từ đầu |
| Ván **online** đang dở | Chuyển `Status = ABORTED`, `EndReason = SERVER_RESTART`. Phòng và đồng hồ đã mất nên không chơi tiếp được, không tính thắng/thua |
| Tài khoản chưa xác nhận OTP | Sau 15 phút nhường biệt danh cho người khác đăng ký; sau 24 giờ tự xoá |

### Đổi cấu trúc bảng (migration)

Cài công cụ một lần:

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
```

Sau khi sửa entity hoặc `AppDbContext`:

```bash
dotnet ef migrations add TenThayDoi
```

Commit cả thư mục `Migrations`. Server tự áp dụng migration mới khi khởi động, người khác chỉ cần `git pull` rồi chạy.

Xem dữ liệu: Visual Studio → View → SQL Server Object Explorer → `(localdb)\MSSQLLocalDB` → Databases → `CoTuongOnline`. Hoặc dùng SQL Server Management Studio.

---

## 6. Luật chơi và bảo mật bổ sung

**Kết thúc ván hoà:**
- Cùng một thế cờ (cùng bên tới lượt) lặp lại **3 lần** → hoà (`REPETITION`).
- **60 nước liền không ăn quân** → hoà (`NO_CAPTURE_LIMIT`).

Đây là luật rút gọn. Luật thi đấu đầy đủ còn xử thua bên chiếu mãi hoặc đuổi bắt mãi, nằm ngoài phạm vi đồ án. Máy (AI) biết tránh đi vào thế hoà do lặp nước khi đang có lợi.

**Chống dò mật khẩu:**
- Một tài khoản đăng nhập sai **5 lần trong 15 phút** → khoá **5 phút** (`AUTH_ACCOUNT_LOCKED`).
- Mỗi địa chỉ IP gọi nhóm API `/api/v1/auth/*` tối đa **30 lần/phút** (`TOO_MANY_REQUESTS`).
- Code nằm ở `Services/Auth/LoginAttemptTracker.cs` và `Infrastructure/RateLimitSetup.cs`.

---

## 7. Những gì đã kiểm thử

- **Sinh nước đi (perft):** đếm số nước từ thế khởi đầu ở độ sâu 1–4 được 44 / 1.920 / 79.666 / 3.290.240, khớp giá trị chuẩn của cờ tướng.
- **Luồng đầy đủ trên 2 trình duyệt:**
  - Đăng ký + OTP.
  - Tạo phòng, mời theo biệt danh, nhận lời mời.
  - Sẵn sàng, bắt đầu, đi quân bằng click và kéo-thả, chat, đầu hàng.
  - Vào phòng bằng mã.
- **Mất kết nối:** banner OFFLINE hiện sau ~15 giây; quay lại thì ván tiếp tục.
- **Hết giờ:** server tự xử thua khi quá thời gian một nước.
- **Ván với máy:** máy đi Khó, đi lại, lưu ván, mở lại ván đã lưu, nạp lại ván từ database sau khi tắt server.
- **Lặp nước:** lặp 3 lần thì hoà, đúng cả khi đi lại và khi nạp ván từ JSON.
- **Bảo mật:** khoá tài khoản sau 5 lần sai, giới hạn 30 request/phút mỗi IP.
- **2 máy thật qua LAN:** chơi online, chat, gọi video 2 chiều bằng camera thật, xử thua khi hết giờ.
- **Giao diện:** đã xem trên desktop 1440px và điện thoại 390px.

Còn cần test trên thiết bị thật: rớt mạng giữa ván, giao diện và gọi video trên điện thoại.
