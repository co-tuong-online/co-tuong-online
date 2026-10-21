using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CoTuongOnline.Features.Rooms;

[Authorize]
public sealed class GameHub(RoomService rooms) : Hub
{
    public async Task<RoomState> Subscribe(string code)
    {
        try {
            var state = await rooms.Get(code, Context.User!.UserId());
            await Groups.AddToGroupAsync(Context.ConnectionId, state.Code);
            // Fetch again after subscribing to close the fetch/subscribe race.
            return await rooms.Get(code, Context.User!.UserId());
        }
        catch (ApiException) { throw new HubException("Không thể truy cập phòng."); }
    }
    public Task Unsubscribe(string code) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomService.Normalize(code));
}
