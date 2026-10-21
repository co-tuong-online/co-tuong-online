using CoTuongOnline.Data;
using CoTuongOnline.Features.Rooms;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoTuongOnline.Features.Invitations;

public sealed record InvitationDto(Guid Id, string Code, string HostUsername, DateTimeOffset CreatedAt);
[ApiController, Authorize, Route("api/invitations")]
public sealed class InvitationsController(AppDbContext db, RoomService rooms) : ControllerBase
{
    [HttpGet]
    public async Task<InvitationDto[]> List() => await db.Invitations
        .Where(i => i.RecipientId == User.UserId() && i.Status == "pending" && i.Room.BlackPlayerId == null)
        .OrderByDescending(i => i.CreatedAt)
        .Select(i => new InvitationDto(i.Id, i.Room.Code, i.Room.Host.UserName!, i.CreatedAt)).ToArrayAsync();
    private async Task<Invitation> Find(Guid id) => await db.Invitations.Include(i => i.Room)
        .SingleOrDefaultAsync(i => i.Id == id && i.RecipientId == User.UserId())
        ?? throw new ApiException(404, "Không tìm thấy lời mời.");
    [HttpPost("{id:guid}/accept")]
    public async Task<RoomState> Accept(Guid id)
    {
        var invitation = await Find(id);
        if (invitation.Status != "pending") throw new ApiException(409, "Lời mời không còn hiệu lực.");
        return await rooms.Join(invitation.Room.Code, User.UserId(), invitation);
    }
    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id)
    {
        var invitation = await Find(id);
        if (invitation.Status != "pending") throw new ApiException(409, "Lời mời không còn hiệu lực.");
        invitation.Status = "declined"; await db.SaveChangesAsync(); return NoContent();
    }
}
