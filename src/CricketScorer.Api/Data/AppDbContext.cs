using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<ScoringSession> ScoringSessions => Set<ScoringSession>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    // Sets up table rules: lengths, unique codes, foreign keys and the event sequence index.
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(80);
            e.HasOne(u => u.School).WithMany().HasForeignKey(u => u.SchoolId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<School>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.JoinCode).HasMaxLength(8).IsFixedLength();
            e.HasIndex(s => s.JoinCode).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Team>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasIndex(t => new { t.SchoolId, t.Name }).IsUnique();
            e.HasOne<School>().WithMany().HasForeignKey(t => t.SchoolId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.Players).WithOne().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Player>(e => e.Property(p => p.Name).HasMaxLength(80));

        b.Entity<Match>(e =>
        {
            e.Property(m => m.ShareCode).HasMaxLength(10).IsFixedLength();
            e.HasIndex(m => m.ShareCode).IsUnique();
            e.Property(m => m.Venue).HasMaxLength(100);
            e.Property(m => m.Format).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.TossDecision).HasConversion<string>().HasMaxLength(10);
            e.HasOne<School>().WithMany().HasForeignKey(m => m.SchoolId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(m => m.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamBId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(m => m.Players).WithOne().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany<ScoringSession>().WithOne().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany<StoredEvent>().WithOne().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MatchPlayer>(e =>
        {
            e.HasKey(p => new { p.MatchId, p.PlayerId });
            e.HasOne<Player>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ScoringSession>(e => e.Property(s => s.DeviceLabel).HasMaxLength(60));

        b.Entity<StoredEvent>(e =>
        {
            e.Property(s => s.Type).HasMaxLength(40);
            e.HasIndex(s => new { s.MatchId, s.Sequence }).IsUnique().HasFilter("[IsRemoved] = 0");
        });
    }
}
