using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoTuongOnline.Data;

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string HostId { get; set; } = "";
    public IdentityUser Host { get; set; } = null!;
    public string? BlackPlayerId { get; set; }
    public IdentityUser? BlackPlayer { get; set; }
    public string BoardJson { get; set; } = "";
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public string RecipientId { get; set; } = "";
    public IdentityUser Recipient { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Room>(r => {
            r.HasIndex(x => x.Code).IsUnique();
            r.Property(x => x.Code).HasMaxLength(8);
            r.Property(x => x.BoardJson).HasColumnType("jsonb");
            r.Property(x => x.Revision).IsConcurrencyToken();
            r.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne(x => x.BlackPlayer).WithMany().HasForeignKey(x => x.BlackPlayerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Invitation>(i => {
            i.HasIndex(x => new { x.RoomId, x.RecipientId }).IsUnique();
            i.Property(x => x.Status).HasMaxLength(16).IsConcurrencyToken();
            i.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
