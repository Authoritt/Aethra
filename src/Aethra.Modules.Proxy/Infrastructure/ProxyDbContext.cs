using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.Infrastructure;

public sealed class ProxyDbContext(DbContextOptions<ProxyDbContext> options) : AethraModuleDbContext(options)
{
    public override string SchemaName => "proxy";

    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new RouteConfiguration());
        modelBuilder.ApplyConfiguration(new CertificateConfiguration());
        modelBuilder.ApplyConfiguration(new AcmeAccountConfiguration());
    }
}
