using System.Text;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomeGed.Api.Data;

/// <summary>
/// Base <c>homeged</c> : données propres à Home-GED uniquement (§3.3), jamais de document.
/// </summary>
public sealed class HomeGedDbContext(DbContextOptions<HomeGedDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    public DbSet<ShareAccessLog> ShareAccessLog => Set<ShareAccessLog>();

    /// <summary>Clés Data Protection : le conteneur est en lecture seule et les sessions survivent aux redéploiements.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(s => s.Id);
            e.Property(s => s.TokenHash).HasMaxLength(32);
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.UserId);
            e.HasIndex(s => s.ExpiresAt);
            e.Property(s => s.Username).HasMaxLength(150);
            e.Property(s => s.Ip).HasMaxLength(45);
            e.Property(s => s.UserAgent).HasMaxLength(512);
        });

        modelBuilder.Entity<ShareLink>(e =>
        {
            e.ToTable("share_links");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasMaxLength(22);
            e.Property(l => l.PasswordHash).HasMaxLength(200);
            e.HasIndex(l => l.OwnerId);
            e.HasIndex(l => l.ExpiresAt);
            e.HasMany(l => l.AccessLog).WithOne().HasForeignKey(a => a.ShareLinkId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShareAccessLog>(e =>
        {
            e.ToTable("share_access_log");
            e.HasKey(a => a.Id);
            e.Property(a => a.ShareLinkId).HasMaxLength(22);
            e.HasIndex(a => new { a.ShareLinkId, a.Timestamp });
            e.Property(a => a.Ip).HasMaxLength(45);
            e.Property(a => a.UserAgent).HasMaxLength(512);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        // Conventions PostgreSQL : noms en snake_case.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? ""));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? ""));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? ""));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
