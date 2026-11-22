using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeGed.Api.Data;

/// <summary>Pour <c>dotnet ef migrations add</c> : aucune connexion n'est ouverte.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HomeGedDbContext>
{
    public HomeGedDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HomeGedDbContext>()
            .UseNpgsql("Host=localhost;Database=homeged")
            .Options);
}
