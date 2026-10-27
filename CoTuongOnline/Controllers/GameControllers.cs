using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Services.Games;
using CoTuongOnline.Services.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoTuongOnline.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly RoomService _rooms;
    public RoomsController(RoomService rooms) => _rooms = rooms;

    /// <summary>Danh sách phòng công khai đang chờ người chơi.</summary>
    [HttpGet]
    public ApiResponse<List<RoomListItem>> List() => ApiResponse.Ok(_rooms.ListOpen());

    /// <summary>Phòng mà người dùng hiện đang ở (để sảnh hiện nút "Quay lại phòng").</summary>
    [HttpGet("current")]
    public ApiResponse<RoomDto?> Current()
    {
        var code = _rooms.CurrentRoomOf(User.GetUserId());
        return ApiResponse.Ok(code is null ? null : _rooms.Get(code));
    }

    [HttpPost]
    public async Task<ApiResponse<RoomDto>> Create(CreateRoomRequest req) =>
        ApiResponse.Ok(await _rooms.CreateAsync(User.GetUserId(), User.GetNickname(), req.Config), "Đã tạo phòng.");

    [HttpGet("{code}")]
    public ApiResponse<RoomDto> Get(string code) => ApiResponse.Ok(_rooms.Get(code));

    /// <summary>Vào phòng bằng mã 6 số hoặc link.</summary>
    [HttpPost("{code}/join")]
    public async Task<ApiResponse<RoomDto>> Join(string code) =>
        ApiResponse.Ok(await _rooms.JoinAsync(User.GetUserId(), User.GetNickname(), code));

    [HttpPost("{code}/leave")]
    public async Task<ApiResponse<object>> Leave(string code)
    {
        await _rooms.LeaveAsync(User.GetUserId(), code);
        return ApiResponse.Ok("Đã rời phòng.");
    }

    [HttpPut("{code}/config")]
    public async Task<ApiResponse<RoomDto>> UpdateConfig(string code, RoomConfigDto config) =>
        ApiResponse.Ok(await _rooms.UpdateConfigAsync(User.GetUserId(), code, config));

    [HttpPost("{code}/ready")]
    public async Task<ApiResponse<RoomDto>> Ready(string code, ReadyRequest req) =>
        ApiResponse.Ok(await _rooms.SetReadyAsync(User.GetUserId(), code, req.Ready));

    [HttpPost("{code}/kick")]
    public async Task<ApiResponse<object>> Kick(string code)
    {
        await _rooms.KickAsync(User.GetUserId(), code);
        return ApiResponse.Ok("Đã mời người chơi ra khỏi phòng.");
    }

    [HttpPost("{code}/start")]
    public async Task<ApiResponse<GameStateDto>> Start(string code) =>
        ApiResponse.Ok(await _rooms.StartAsync(User.GetUserId(), code), "Ván cờ bắt đầu.");

    [HttpGet("{code}/messages")]
    public ApiResponse<List<ChatMessageDto>> Messages(string code) =>
        ApiResponse.Ok(_rooms.GetMessages(User.GetUserId(), code));
}

[ApiController]
[Authorize]
[Route("api/v1/games")]
public sealed class GamesController : ControllerBase
{
    private readonly OnlineGameService _games;
    public GamesController(OnlineGameService games) => _games = games;

    /// <summary>Toàn bộ trạng thái ván online (thế cờ, lượt, đồng hồ) — dùng khi tải lại trang / kết nối lại.</summary>
    [HttpGet("{id:long}/state")]
    public ApiResponse<GameStateDto> State(long id) => ApiResponse.Ok(_games.GetState(id, User.GetUserId()));
}

[ApiController]
[Authorize]
[Route("api/v1/ai-games")]
public sealed class AiGamesController : ControllerBase
{
    private readonly AiGameService _ai;
    public AiGamesController(AiGameService ai) => _ai = ai;

    [HttpPost]
    public async Task<ApiResponse<AiGameDto>> Create(CreateAiGameRequest req) =>
        ApiResponse.Ok(await _ai.CreateAsync(User.GetUserId(), req.Level, req.PlayerColor));

    [HttpGet("{id:long}")]
    public async Task<ApiResponse<AiGameDto>> Get(long id) => ApiResponse.Ok(await _ai.GetAsync(User.GetUserId(), id));

    /// <summary>Nước đi của người chơi. Sau đó client gọi /ai-move để máy đi.</summary>
    [HttpPost("{id:long}/moves")]
    public async Task<ApiResponse<AiMoveResult>> Move(long id, MoveRequest req) =>
        ApiResponse.Ok(await _ai.PlayerMoveAsync(User.GetUserId(), id, req.From, req.To));

    [HttpPost("{id:long}/ai-move")]
    public async Task<ApiResponse<AiMoveResult>> AiMove(long id) =>
        ApiResponse.Ok(await _ai.AiMoveAsync(User.GetUserId(), id));

    [HttpPost("{id:long}/undo")]
    public async Task<ApiResponse<AiGameDto>> Undo(long id) => ApiResponse.Ok(await _ai.UndoAsync(User.GetUserId(), id));

    [HttpPost("{id:long}/resign")]
    public async Task<ApiResponse<AiGameDto>> Resign(long id) =>
        ApiResponse.Ok(await _ai.ResignAsync(User.GetUserId(), id));
}

[ApiController]
[Authorize]
[Route("api/v1/saved-games")]
public sealed class SavedGamesController : ControllerBase
{
    private readonly SavedGameService _saved;
    public SavedGamesController(SavedGameService saved) => _saved = saved;

    [HttpGet]
    public async Task<ApiResponse<List<SavedGameDto>>> List() => ApiResponse.Ok(await _saved.ListAsync(User.GetUserId()));

    [HttpPost]
    public async Task<ApiResponse<SavedGameDto>> Save(SaveGameRequest req) =>
        ApiResponse.Ok(await _saved.SaveAsync(User.GetUserId(), req), "Đã lưu ván cờ.");

    [HttpPatch("{id:long}")]
    public async Task<ApiResponse<SavedGameDto>> Rename(long id, RenameSavedGameRequest req) =>
        ApiResponse.Ok(await _saved.RenameAsync(User.GetUserId(), id, req.Name), "Đã đổi tên.");

    [HttpDelete("{id:long}")]
    public async Task<ApiResponse<object>> Delete(long id)
    {
        await _saved.DeleteAsync(User.GetUserId(), id);
        return ApiResponse.Ok("Đã xoá ván lưu.");
    }

    /// <summary>Tiếp tục ván đã lưu → trả về ván với máy mới.</summary>
    [HttpPost("{id:long}/resume")]
    public async Task<ApiResponse<AiGameDto>> Resume(long id) =>
        ApiResponse.Ok(await _saved.ResumeAsync(User.GetUserId(), id));
}
