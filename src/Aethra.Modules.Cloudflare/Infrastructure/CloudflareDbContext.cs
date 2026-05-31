using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.Infrastructure;

public sealed class CloudflareDbContext(DbContextOptions<CloudflareDbContext> options) : AethraModuleDbContext(options)
{
    public override string SchemaName => "cloudflare";

    public DbSet<CloudflareZone> Zones => Set<CloudflareZone>();
    public DbSet<DnsRecord> DnsRecords => Set<DnsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new CloudflareZoneConfiguration());
        modelBuilder.ApplyConfiguration(new DnsRecordConfiguration());
    }
}
