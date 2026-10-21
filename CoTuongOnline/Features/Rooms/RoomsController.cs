using System.ComponentModel.DataAnnotations;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoTuongOnline.Features.Rooms;

public sealed record MoveRequest([Required, MaxLength(40)] string PieceId, [Range(0,9)] int Row, [Range(0,8)] int Col, [Range(0,long.MaxValue)] long ExpectedRevision);
public sealed record InviteRequest([Required, MaxLength(32)] string Username);

[ApiController, Authorize, Route("api/rooms")]
public sealed class RoomsController(RoomService rooms) : ControllerBase
{
    [HttpGet] public Task<RoomState[]> List() => rooms.List(User.UserId());
    [HttpPost] public Task<RoomState> Create() => rooms.Create(User.UserId());
    [HttpGet("{code}")] public Task<RoomState> Get(string code) => rooms.Get(code, User.UserId());
    [HttpPost("{code}/join")] public Task<RoomState> Join(string code) => rooms.Join(code, User.UserId());
    [HttpPost("{code}/moves")] public Task<RoomState> Move(string code, MoveRequest request) => rooms.Move(code, User.UserId(), request);
    [HttpPost("{code}/invitations")] public async Task<IActionResult> Invite(string code, InviteRequest request)
    {
        await rooms.Invite(code, User.UserId(), request.Username); return NoContent();
    }
}
