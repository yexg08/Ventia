using Ventia.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ventia.Infrastructure.Persistence;

/// <summary>solo para `dotnet ef migrations` (no necesita la API ni una sesion)</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5435;Database=ventia;Username=ventia;Password=ventia_dev_password")
            .Options;
        return new AppDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
        public void Set(Guid? tenantId) { }
    }
}
