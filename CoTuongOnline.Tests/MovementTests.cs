using CoTuongOnline.Features.Games;
using Xunit;

namespace CoTuongOnline.Tests;

public sealed class MovementTests
{
    private readonly MovementEngine engine = new();
    private static Piece P(string type, string color, int row, int col, string id = "p") => new(id, type, color, "test.svg", row, col);
    public static IEnumerable<object[]> Geometry()
    {
        // type, color, start row/col, destination row/col, legal
        yield return new object[] { "xe", "do", 5, 4, 5, 8, true };
        yield return new object[] { "xe", "do", 5, 4, 2, 4, true };
        yield return new object[] { "xe", "do", 5, 4, 6, 5, false };
        yield return new object[] { "phao", "do", 5, 4, 1, 4, true };
        yield return new object[] { "phao", "do", 5, 4, 4, 3, false };
        yield return new object[] { "ma", "do", 5, 4, 3, 3, true };
        yield return new object[] { "ma", "do", 5, 4, 6, 6, true };
        yield return new object[] { "ma", "do", 5, 4, 7, 4, false };
        yield return new object[] { "tuong", "do", 9, 2, 7, 4, true };
        yield return new object[] { "tuong", "do", 5, 4, 3, 2, false };
        yield return new object[] { "tuong", "den", 0, 2, 2, 4, true };
        yield return new object[] { "tuong", "den", 4, 2, 6, 4, false };
        yield return new object[] { "si", "do", 9, 3, 8, 4, true };
        yield return new object[] { "si", "do", 8, 3, 7, 2, false };
        yield return new object[] { "si", "den", 2, 5, 3, 4, false };
        yield return new object[] { "chu_tuong", "do", 9, 4, 8, 4, true };
        yield return new object[] { "chu_tuong", "den", 2, 4, 3, 4, false };
        yield return new object[] { "chu_tuong", "do", 8, 3, 8, 2, false };
        yield return new object[] { "chu_tuong", "do", 8, 4, 7, 3, false };
        yield return new object[] { "tot", "do", 6, 0, 5, 0, true };
        yield return new object[] { "tot", "do", 6, 0, 6, 1, false };
        yield return new object[] { "tot", "do", 4, 0, 4, 1, true };
        yield return new object[] { "tot", "do", 4, 0, 5, 0, false };
        yield return new object[] { "tot", "den", 3, 0, 4, 0, true };
        yield return new object[] { "tot", "den", 4, 0, 4, 1, false };
        yield return new object[] { "tot", "den", 5, 0, 5, 1, true };
        yield return new object[] { "tot", "den", 5, 0, 4, 0, false };
    }
    [Theory, MemberData(nameof(Geometry))]
    public void PieceGeometry(string type, string color, int row, int col, int destRow, int destCol, bool legal)
    {
        var board = new Board(new() { P(type, color, row, col) }, color);
        if (legal) Assert.Equal(destRow, engine.Move(board, color, "p", destRow, destCol).Pieces.Single().Row);
        else Assert.Throws<InvalidOperationException>(() => engine.Move(board, color, "p", destRow, destCol));
    }
    [Theory]
    [InlineData("xe", 5, 4, 1, 4, 3, 4)]
    [InlineData("phao", 5, 4, 1, 4, 3, 4)]
    [InlineData("ma", 5, 4, 3, 3, 4, 4)]
    [InlineData("ma", 5, 4, 6, 6, 5, 5)]
    [InlineData("tuong", 9, 2, 7, 4, 8, 3)]
    public void BlockersRejectMovement(string type, int r, int c, int tr, int tc, int br, int bc)
    {
        var board = new Board(new() { P(type, "do", r, c), P("tot", "den", br, bc, "blocker") });
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "do", "p", tr, tc));
    }
    [Theory, InlineData(0, false), InlineData(1, true), InlineData(2, false)]
    public void CannonRequiresExactlyOneScreenToCapture(int screens, bool legal)
    {
        var pieces = new List<Piece> { P("phao", "do", 8, 4), P("xe", "den", 1, 4, "target") };
        for (int i = 0; i < screens; i++) pieces.Add(P("tot", "do", i + 3, 4, $"screen{i}"));
        if (legal) Assert.DoesNotContain(engine.Move(new(pieces), "do", "p", 1, 4).Pieces, p => p.Id == "target");
        else Assert.Throws<InvalidOperationException>(() => engine.Move(new(pieces), "do", "p", 1, 4));
    }
    [Fact]
    public void CapturesSwitchTurnsWithoutMutatingInput()
    {
        var board = new Board(new() { P("xe", "do", 5, 4), P("tot", "den", 1, 4, "target") });
        var next = engine.Move(board, "do", "p", 1, 4);
        Assert.Equal("den", next.Turn); Assert.Single(next.Pieces); Assert.Equal(2, board.Pieces.Count);
        Assert.Equal(5, board.Pieces[0].Row);
    }
    [Fact]
    public void CapturingGeneralFinishesGame()
    {
        var next = engine.Move(new(new() { P("xe", "do", 5, 4), P("chu_tuong", "den", 0, 4, "general") }), "do", "p", 0, 4);
        Assert.Equal("finished", next.Status); Assert.Equal("do", next.Winner);
        Assert.Throws<InvalidOperationException>(() => engine.Move(next, "den", "p", 1, 4));
    }
    [Fact]
    public void RejectsWrongTurnOwnershipFriendlyCaptureAndBounds()
    {
        var board = Board.Initial();
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "den", "tot_den_1", 4, 0));
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "do", "tot_den_1", 4, 0));
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "do", "xe_do_1", 9, 1));
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "do", "tot_do_1", -1, 0));
        Assert.Throws<InvalidOperationException>(() => engine.Move(board, "do", "missing", 5, 0));
    }
    [Fact]
    public void InitialBoardHas32UniquePiecesAndRoundTrips()
    {
        var board = Board.Initial(); Assert.Equal(32, board.Pieces.Count);
        Assert.Equal(32, board.Pieces.Select(p => (p.Row, p.Col)).Distinct().Count());
        Assert.Equal(board.Serialize(), Board.Read(board.Serialize()).Serialize());
    }
}
