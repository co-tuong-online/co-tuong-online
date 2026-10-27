using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoTuongOnline.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nickname = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    GamesPlayed = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    Draws = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomCode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    RedPlayerId = table.Column<int>(type: "int", nullable: true),
                    BlackPlayerId = table.Column<int>(type: "int", nullable: true),
                    OpponentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AiLevel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CurrentStateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.Id);
                    table.CheckConstraint("CK_games_state_json", "ISJSON([CurrentStateJson]) = 1");
                    table.ForeignKey(
                        name: "FK_games_users_BlackPlayerId",
                        column: x => x.BlackPlayerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_games_users_RedPlayerId",
                        column: x => x.RedPlayerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_games",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OpponentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AiLevel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PlayerColor = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MoveCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_games", x => x.Id);
                    table.CheckConstraint("CK_saved_games_state_json", "ISJSON([StateJson]) = 1");
                    table.ForeignKey(
                        name: "FK_saved_games_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_moves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    Ply = table.Column<int>(type: "int", nullable: false),
                    FromRow = table.Column<int>(type: "int", nullable: false),
                    FromCol = table.Column<int>(type: "int", nullable: false),
                    ToRow = table.Column<int>(type: "int", nullable: false),
                    ToCol = table.Column<int>(type: "int", nullable: false),
                    Piece = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Captured = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Notation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SpentMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_moves_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_moves_GameId_Ply",
                table: "game_moves",
                columns: new[] { "GameId", "Ply" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_games_BlackPlayerId",
                table: "games",
                column: "BlackPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_games_RedPlayerId",
                table: "games",
                column: "RedPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_games_StartedAt",
                table: "games",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_otp_codes_Email_Purpose_CreatedAt",
                table: "otp_codes",
                columns: new[] { "Email", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_games_UserId",
                table: "saved_games",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Nickname",
                table: "users",
                column: "Nickname",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_moves");

            migrationBuilder.DropTable(
                name: "otp_codes");

            migrationBuilder.DropTable(
                name: "saved_games");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
