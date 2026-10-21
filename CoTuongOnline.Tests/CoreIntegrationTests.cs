using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CoTuongOnline.Data;
using CoTuongOnline.Features.Rooms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace CoTuongOnline.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COTUONG_TEST_DATABASE"))) Skip = "Set COTUONG_TEST_DATABASE to a disposable PostgreSQL connection (CREATE DATABASE permission required)."; }
}
public sealed class TestApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}
public sealed class CoreIntegrationTests
{
    private const string Key = "integration-test-only-key-over-thirty-two-bytes";
    private sealed record Login(UserInfo User, string AccessToken);
    private sealed record UserInfo(string Id, string Username);
    private sealed record Csrf(string Token);
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (csrf) {
            var token = (await client.GetFromJsonAsync<Csrf>("/api/auth/csrf"))!.Token;
            request.Headers.Add("X-CSRF-TOKEN", token);
        }
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task<Login> Register(HttpClient client, string name)
    {
        var response = await Post(client, "/api/auth/register", new { username = name, password = "testpass123" });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Login>())!;
    }
    private static async Task<RoomState> State(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<RoomState>())!;
    }
    private static HubConnection Hub(TestApp app, string token) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/game", options => {
        options.Transports = HttpTransportType.LongPolling;
        options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
    }).Build();

    [PostgreSqlFact]
    public async Task CoreFlowsAgainstRealPostgreSql()
    {
        var root = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COTUONG_TEST_DATABASE"));
        string database = "cotuong_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(root.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        root.Database = database;
        var oldConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var oldKey = Environment.GetEnvironmentVariable("Jwt__Key");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", root.ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", Key);
        try {
            using var app = new TestApp();
            using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            using var alice = app.CreateClient(); using var bob = app.CreateClient(); using var carol = app.CreateClient();
            var a = await Register(alice, "alice"); var b = await Register(bob, "bob"); var c = await Register(carol, "carol");
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(carol, "/api/auth/register", new { username = "ALICE", password = "testpass123" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(carol, "/api/auth/login", new { username = "alice", password = "incorrect" })).StatusCode);
            using var anonymous = app.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rooms")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(alice, "/api/rooms", csrf: false)).StatusCode);
            var room = await State(await Post(alice, "/api/rooms")); Assert.Equal("waiting", room.Status);
            Assert.Equal(32, room.Board.Pieces.Count);
            Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync($"/api/rooms/{room.Code}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(alice, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_do_1", 5, 0, room.Revision))).StatusCode);
            (await Post(alice, $"/api/rooms/{room.Code}/invitations", new { username = "BOB" })).EnsureSuccessStatusCode();
            var invitations = (await bob.GetFromJsonAsync<List<InvitationDtoForTest>>("/api/invitations"))!;
            Assert.Single(invitations);
            room = await State(await Post(bob, $"/api/invitations/{invitations[0].Id}/accept"));
            Assert.Equal(2, room.Players.Length); Assert.Equal("active", room.Status);
            Assert.Empty((await bob.GetFromJsonAsync<List<InvitationDtoForTest>>("/api/invitations"))!);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(carol, $"/api/rooms/{room.Code}/join")).StatusCode);
            Assert.Equal(room.Revision, (await State(await Post(bob, $"/api/rooms/{room.Code.ToLowerInvariant()}/join"))).Revision);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(bob, $"/api/rooms/{room.Code}/invitations", new { username = "carol" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(bob, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_den_1", 4, 0, room.Revision))).StatusCode);
            await using var ah = Hub(app, a.AccessToken); await using var bh = Hub(app, b.AccessToken);
            await ah.StartAsync(); await bh.StartAsync();
            var isolatedRoom = await State(await Post(carol, "/api/rooms"));
            await using var ch = Hub(app, c.AccessToken); await ch.StartAsync();
            await ch.InvokeAsync<RoomState>("Subscribe", isolatedRoom.Code);
            int leakedEvents = 0; ch.On<RoomState>("RoomUpdated", _ => Interlocked.Increment(ref leakedEvents));
            await Assert.ThrowsAsync<HubException>(() => bh.InvokeAsync<RoomState>("Subscribe", isolatedRoom.Code));
            await ah.InvokeAsync<RoomState>("Subscribe", room.Code); await bh.InvokeAsync<RoomState>("Subscribe", room.Code);
            var delivered = new TaskCompletionSource<RoomState>(TaskCreationOptions.RunContinuationsAsynchronously);
            bh.On<RoomState>("RoomUpdated", s => delivered.TrySetResult(s));
            var moved = await State(await Post(alice, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_do_1", 5, 0, room.Revision)));
            var broadcast = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(moved.Revision, broadcast.Revision); Assert.Equal("den", moved.Board.Turn);
            Assert.Equal(0, Volatile.Read(ref leakedEvents));
            Assert.Equal(HttpStatusCode.Conflict, (await Post(alice, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_do_1", 5, 0, room.Revision))).StatusCode);
            using (var restarted = new TestApp()) {
                using var client = restarted.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", b.AccessToken);
                var loaded = (await client.GetFromJsonAsync<RoomState>($"/api/rooms/{room.Code}"))!;
                Assert.Equal(moved.Revision, loaded.Revision); Assert.Equal(5, loaded.Board.Pieces.Single(p => p.Id == "tot_do_1").Row);
            }
            // A database write failure must not advance the board or send a notification.
            await using var sql = new NpgsqlConnection(root.ConnectionString); await sql.OpenAsync();
            const string failSql = "CREATE FUNCTION reject_room_update() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test failure'; END $$; CREATE TRIGGER reject_room BEFORE UPDATE ON \"Rooms\" FOR EACH ROW EXECUTE FUNCTION reject_room_update();";
            await using (var fail = new NpgsqlCommand(failSql, sql)) await fail.ExecuteNonQueryAsync();
            Assert.Equal(HttpStatusCode.InternalServerError, (await Post(bob, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_den_1", 4, 0, moved.Revision))).StatusCode);
            await using (var undo = new NpgsqlCommand("DROP TRIGGER reject_room ON \"Rooms\"; DROP FUNCTION reject_room_update();", sql)) await undo.ExecuteNonQueryAsync();
            Assert.Equal(moved.Revision, (await alice.GetFromJsonAsync<RoomState>($"/api/rooms/{room.Code}"))!.Revision);
            // Concurrent legal moves based on one revision: exactly one can commit.
            var moves = await Task.WhenAll(Post(bob, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_den_1", 4, 0, moved.Revision)),
                Post(bob, $"/api/rooms/{room.Code}/moves", new MoveRequest("tot_den_2", 4, 2, moved.Revision)));
            Assert.Single(moves, x => x.IsSuccessStatusCode); Assert.Single(moves, x => x.StatusCode == HttpStatusCode.Conflict);
            var otherRoom = await State(await Post(alice, "/api/rooms"));
            var claims = await Task.WhenAll(Post(bob, $"/api/rooms/{otherRoom.Code}/join"), Post(carol, $"/api/rooms/{otherRoom.Code}/join"));
            Assert.Single(claims, x => x.IsSuccessStatusCode); Assert.Single(claims, x => x.StatusCode == HttpStatusCode.Conflict);
            // Users outside a room cannot subscribe, and cross-origin handshakes are rejected.
            await Assert.ThrowsAsync<HubException>(() => bh.InvokeAsync<RoomState>("Subscribe", "ZZZZZZZZ"));
            using var origin = new HttpRequestMessage(HttpMethod.Post, "/hubs/game/negotiate?negotiateVersion=1"); origin.Headers.Add("Origin", "https://other.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await alice.SendAsync(origin)).StatusCode);
            using var bearer = app.CreateClient(); bearer.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
            Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/auth/me")).StatusCode);
            var expired = new JwtSecurityToken("CoTuongOnline", "CoTuongOnline", expires: DateTime.UtcNow.AddMinutes(-2),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            bearer.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(expired));
            Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/auth/me")).StatusCode);
            await ah.StopAsync(); await bh.StopAsync(); await ch.StopAsync();
            (await Post(alice, "/api/auth/logout")).EnsureSuccessStatusCode();
            bearer.DefaultRequestHeaders.Authorization = new("Bearer", a.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/rooms/join/{room.Code}")).StatusCode);
        }
        finally {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", oldConnection); Environment.SetEnvironmentVariable("Jwt__Key", oldKey);
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }
    private sealed record InvitationDtoForTest(Guid Id);
}
