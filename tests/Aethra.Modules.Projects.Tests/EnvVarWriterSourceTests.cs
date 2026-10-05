using System.Text;
using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Regresión: PUT /api/env-vars devolvía 500 (23505 ux_env_vars_scope_key) al actualizar una var
/// creada con otra Source. El índice único no incluye source, así que el writer debe decidir
/// update/preservar en vez de insertar duplicados. (InMemory no impone el índice: se asserta 1 fila.)
/// </summary>
public sealed class EnvVarWriterSourceTests(ProjectsDbFixture fixture) : IClassFixture<ProjectsDbFixture>
{
    private const string Instance = "ins_test";
    private const string Manual = "manual:api";
    private const string Mcp = "mcp:apikey:abc";
    private const string Binding = "binding:bnd_1";

    private static EnvVarUpsert V(string value) => new("CSP_MODO", value, false, true);

    private static async Task<EnvironmentVariable> Single(Aethra.Modules.Projects.Infrastructure.ProjectsDbContext db) =>
        await db.EnvironmentVariables.SingleAsync(e => e.ScopeId == Instance && e.Key == "CSP_MODO");

    [Fact]
    public async Task Manual_crea_mcp_actualiza_una_fila_valor_nuevo()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfEnvVarWriter(db, new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [V("a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Mcp, [V("b")], default);
        var row = await Single(db);
        row.Value.Should().Be("b");
        row.Source.Should().Be(Mcp);
    }

    [Fact]
    public async Task Manual_crea_binding_actualiza_valor_manual_intacto()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfEnvVarWriter(db, new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [V("a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Binding, [V("b")], default);
        var row = await Single(db);
        row.Value.Should().Be("a");
        row.Source.Should().Be(Manual);
    }

    [Fact]
    public async Task Binding_crea_manual_actualiza_toma_valor_y_source_manual()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfEnvVarWriter(db, new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Binding, [V("a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [V("b")], default);
        var row = await Single(db);
        row.Value.Should().Be("b");
        row.Source.Should().Be(Manual);
    }

    [Fact]
    public async Task Misma_source_actualiza()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfEnvVarWriter(db, new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [V("a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [V("b")], default);
        var row = await Single(db);
        row.Value.Should().Be("b");
        row.Source.Should().Be(Manual);
    }

    [Fact]
    public async Task Secret_manual_crea_mcp_actualiza_una_fila()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfSecretWriter(db, new Utf8Codec(), new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [new SecretUpsert("K", "a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Mcp, [new SecretUpsert("K", "b")], default);
        var row = await db.Secrets.SingleAsync(s => s.ScopeId == Instance && s.Key == "K");
        Encoding.UTF8.GetString(row.ValueCipher).Should().Be("b");
        row.Source.Should().Be(Mcp);
    }

    [Fact]
    public async Task Secret_manual_crea_binding_no_pisa()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var w = new EfSecretWriter(db, new Utf8Codec(), new SystemClock());
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Manual, [new SecretUpsert("K", "a")], default);
        await w.UpsertManyAsync(EnvVarScope.Instance, Instance, Binding, [new SecretUpsert("K", "b")], default);
        var row = await db.Secrets.SingleAsync(s => s.ScopeId == Instance && s.Key == "K");
        Encoding.UTF8.GetString(row.ValueCipher).Should().Be("a");
        row.Source.Should().Be(Manual);
    }

    private sealed class Utf8Codec : ISecretCodec
    {
        public byte[] Encode(string plainValue) => Encoding.UTF8.GetBytes(plainValue);
        public string Decode(byte[] cipher) => Encoding.UTF8.GetString(cipher);
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
