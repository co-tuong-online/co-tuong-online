using _23DTHE3_DemoCoTuong.Models;
using Microsoft.AspNetCore.SignalR;
namespace _23DTHE3_DemoCoTuong.Hubs
{
    public class ChessHub: Hub
    {

        public async Task MovePiece(
            string roomId,
            string pieceId,
            int fromRow,
            int fromCol,
            int toRow,
            int toCol)
        {
            var move = new ChessMoveModel
            {
                PieceId = pieceId,

                FromRow = fromRow,
                FromCol = fromCol,

                ToRow = toRow,
                ToCol = toCol
            };


            Console.WriteLine(
                $"Room: {roomId} | " +
                $"{pieceId}: " +
                $"({fromRow},{fromCol}) -> " +
                $"({toRow},{toCol})"
            );


            // Gửi nước đi cho tất cả người trong room
            await Clients.All
                //.Group(roomId)
                .SendAsync(
                    "ReceiveMove",
                    move
                );
        }
        public async Task SendMessage(
          string roomId,
          string userName,
          string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            message = message.Trim();


            await Clients
                .All
                .SendAsync(
                    "ReceiveMessage",
                    userName,
                    message
                );
        }

        public async Task SendOffer(
       string roomId,
       string offer)
        {
            await Clients.All
               // .OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveOffer",
                    offer
                );
        }


        // Người nhận gửi Answer
        public async Task SendAnswer(
            string roomId,
            string answer)
        {
            await Clients.All
                //.OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveAnswer",
                    answer
                );
        }


        // Trao đổi ICE Candidate
        public async Task SendIceCandidate(
            string roomId,
            string candidate)
        {
            await Clients.All
                //.OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveIceCandidate",
                    candidate
                );
        }
    }
}
