using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.Infrastructure;

public sealed class VmsDbContext(DbContextOptions<VmsDbContext> options) : AethraModuleDbContext(options)
{
    public override string SchemaName => "vms";

    public DbSet<Vm> Vms => Set<Vm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new VmConfiguration());
    }
}
