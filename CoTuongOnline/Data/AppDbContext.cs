using CoTuongOnline.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CoTuongOnline.Data;

/// <summary>
/// Cơ sở dữ liệu SQL Server (EF Core Code First). 5 bảng: users, otp_codes, games, game_moves, saved_games.
/// Đổi cấu trúc bảng: sửa Models/Entities.cs hoặc OnModelCreating, rồi chạy
///   dotnet ef migrations add TenThayDoi
/// Server sẽ tự áp dụng migration khi khởi động.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<GameRecord> Games => Set<GameRecord>();
    public DbSet<GameMoveRecord> GameMoves => Set<GameMoveRecord>();
    public DbSet<SavedGame> SavedGames => Set<SavedGame>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(100).IsRequired();
            e.Property(x => x.Nickname).HasMaxLength(20).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Nickname).IsUnique();
        });

        b.Entity<OtpCode>(e =>
        {
            e.ToTable("otp_codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(100).IsRequired();
            e.Property(x => x.Purpose).HasMaxLength(20).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.Email, x.Purpose, x.CreatedAt });
        });

        b.Entity<GameRecord>(e =>
        {
            e.ToTable("games", t => t.HasCheckConstraint("CK_games_state_json", "ISJSON([CurrentStateJson]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.RoomCode).HasMaxLength(6);
            e.Property(x => x.OpponentType).HasMaxLength(10).IsRequired();
            e.Property(x => x.AiLevel).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Result).HasMaxLength(20);
            e.Property(x => x.EndReason).HasMaxLength(30);
            e.Property(x => x.CurrentStateJson).HasColumnType("nvarchar(max)").IsRequired();
            // 2 khoá ngoại cùng trỏ về users → SQL Server không cho cả 2 cùng CASCADE, nên dùng Restrict
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RedPlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlackPlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<GameMoveRecord>(e =>
        {
            e.ToTable("game_moves");
            e.HasKey(x => x.Id);
            e.Property(x => x.Piece).HasMaxLength(2).IsRequired();
            e.Property(x => x.Captured).HasMaxLength(2);
            e.Property(x => x.Notation).HasMaxLength(40).IsRequired();
            e.HasOne<GameRecord>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.GameId, x.Ply }).IsUnique();
        });

        b.Entity<SavedGame>(e =>
        {
            e.ToTable("saved_games", t => t.HasCheckConstraint("CK_saved_games_state_json", "ISJSON([StateJson]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.OpponentType).HasMaxLength(10).IsRequired();
            e.Property(x => x.AiLevel).HasMaxLength(10).IsRequired();
            e.Property(x => x.PlayerColor).HasMaxLength(10).IsRequired();
            e.Property(x => x.StateJson).HasColumnType("nvarchar(max)").IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        // Mọi thời gian đều lưu theo UTC. SQL Server không lưu "loại" thời gian nên khi đọc ra
        // phải đánh dấu lại là UTC, nếu không frontend sẽ hiển thị lệch 7 tiếng.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                if (prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?))
                    prop.SetValueConverter(utc);
    }
}
