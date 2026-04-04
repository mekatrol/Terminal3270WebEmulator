using Microsoft.EntityFrameworkCore;
using Terminal.Data.Models;

namespace Terminal.Data.Context;

/// <summary>
/// Provides Entity Framework Core access to the terminal application's runtime
/// persistence model.
/// </summary>
/// <remarks>
/// The initial schema is intentionally small and focused on live-session
/// tracking so the API can evolve session management behavior before more
/// durable storage is introduced. The context centralizes schema mapping so the
/// host can consume the data layer through dependency injection without knowing
/// individual table details.
/// </remarks>
public sealed class TerminalDataContext(DbContextOptions<TerminalDataContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Gets the users known to the terminal application.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Gets the terminal sessions tracked by the application.
    /// </summary>
    public DbSet<TerminalSession> TerminalSessions => Set<TerminalSession>();

    /// <summary>
    /// Configures the persistence model for the terminal application.
    /// </summary>
    /// <param name="modelBuilder">
    /// The EF Core model builder used to define entity shape, keys, and
    /// relationships.
    /// </param>
    /// <remarks>
    /// Explicit table and key mapping keeps the persistence contract stable and
    /// easy to reason about, which matters if the project later replaces the
    /// in-memory provider with a relational provider.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.UserId);

            entity.Property(user => user.UserId)
                .IsRequired();

            entity.Property(user => user.UserName)
                .IsRequired();

            entity.HasMany(user => user.TerminalSessions)
                .WithOne(session => session.User)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TerminalSession>(entity =>
        {
            entity.ToTable("TerminalSessions");
            entity.HasKey(session => session.TerminalSessionId);

            entity.Property(session => session.CreatedDateTimeUtc)
                .IsRequired();

            entity.Property(session => session.IsActive)
                .IsRequired();

            entity.Property(session => session.UserId)
                .IsRequired();
        });
    }
}
