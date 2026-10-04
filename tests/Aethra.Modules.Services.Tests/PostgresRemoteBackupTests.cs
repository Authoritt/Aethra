using System.IO.Compression;
using System.Text;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Backup;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// OT-008 T10 — backup/restore de un Postgres gestionado que vive en una VM satélite ≠ la del central:
/// el dump se hace DENTRO del contenedor por la RPC de exec; el servicio de la VM del central conserva
/// el camino Npgsql de siempre (regla dura: yunke-redis/relaycore-redis y demás de principal no cambian).
/// </summary>
public sealed class PostgresRemoteBackupTests
{
    private const string Central = "vm_central";
    private const string Bogota = "vm_bogota";
    private const string Password = "pw-Zq9!x'y";

    // ---- regla pura -------------------------------------------------------------------------------

    [Theory]
    [InlineData("vm_bogota", "vm_central", true)]
    [InlineData("vm_central", "vm_central", false)]
    [InlineData("VM_CENTRAL", "vm_central", false)]
    [InlineData("vm_bogota", null, false)]
    [InlineData("vm_bogota", "  ", false)]
    [InlineData("", "vm_central", false)]
    [InlineData(null, "vm_central", false)]
    public void RunsOnRemoteSatellite_only_when_central_known_and_different(string? target, string? central, bool expected)
        => BackupExecutionRules.RunsOnRemoteSatellite(target, central).Should().Be(expected);

    [Fact]
    public void ShellQuote_escapes_single_quotes()
        => BackupExecutionRules.ShellQuote("a'b").Should().Be("'a'\\''b'");

    [Fact]
    public void DecodeDumpOutput_rejects_oversize_marker_explicitly()
    {
        var act = () => BackupExecutionRules.DecodeDumpOutput(3, false, "", "aethra-dump-too-large:99999999");
        act.Should().Throw<InvalidOperationException>().WithMessage("*supera*no se trunca*");
    }

    [Fact]
    public void DecodeDumpOutput_rejects_failure_timeout_nonbase64_and_nongzip()
    {
        var gz = Gzip(Valid("x"));
        var ok = Convert.ToBase64String(gz);
        BackupExecutionRules.DecodeDumpOutput(0, false, ok + "\n", "").Should().Equal(gz);
        ((Action)(() => BackupExecutionRules.DecodeDumpOutput(1, false, "", "boom"))).Should().Throw<InvalidOperationException>().WithMessage("*exit 1*boom*");
        ((Action)(() => BackupExecutionRules.DecodeDumpOutput(-1, true, "", ""))).Should().Throw<InvalidOperationException>().WithMessage("*timeout*");
        ((Action)(() => BackupExecutionRules.DecodeDumpOutput(0, false, "!!!", ""))).Should().Throw<InvalidOperationException>().WithMessage("*base64*");
        ((Action)(() => BackupExecutionRules.DecodeDumpOutput(0, false, Convert.ToBase64String([1, 2, 3]), ""))).Should().Throw<InvalidOperationException>().WithMessage("*gzip*");
        ((Action)(() => BackupExecutionRules.DecodeDumpOutput(0, false, "", ""))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureRestorableRemote_rejects_local_copy_format_and_garbage()
    {
        ((Action)(() => BackupExecutionRules.EnsureRestorableRemote(Gzip("-- aethra-pg-dump v1\nCOPY x FROM STDIN;\n"))))
            .Should().Throw<InvalidOperationException>().WithMessage("*remote v2*");
        ((Action)(() => BackupExecutionRules.EnsureRestorableRemote([1, 2, 3])))
            .Should().Throw<InvalidOperationException>().WithMessage("*gzip*");
        BackupExecutionRules.EnsureRestorableRemote(Gzip(Valid("SELECT 1;")));
    }

    [Fact]
    public void EnsureRestorableRemote_rejects_remote_v1_explicitly_and_dump_without_end_marker()
    {
        // v1 (T11) solo cubria una base: restaurarlo daria un "ok" sin las demas bases.
        ((Action)(() => BackupExecutionRules.EnsureRestorableRemote(
                Gzip(BackupExecutionRules.RemoteDumpHeaderV1 + "\nSELECT 1;\n" + BackupExecutionRules.RemoteDumpEnd + "\n"))))
            .Should().Throw<InvalidOperationException>().WithMessage("*remote v1*UNA base*");
        // gzip integro + cabecera v2 pero SIN la linea final: dump cortado por una etapa caida.
        ((Action)(() => BackupExecutionRules.EnsureRestorableRemote(
                Gzip(BackupExecutionRules.RemoteDumpHeader + "\nSELECT 1;\n"))))
            .Should().Throw<InvalidOperationException>().WithMessage("*incompleto*");
        // la marca final se busca al final de un script grande (cola > buffer)
        BackupExecutionRules.EnsureRestorableRemote(Gzip(Valid(RandomText(400_000))));
    }

    [Fact]
    public void EnsureRestorableRemote_rejects_over_limit()
    {
        var big = new byte[BackupExecutionRules.MaxCompressedDumpBytes + 1];
        ((Action)(() => BackupExecutionRules.EnsureRestorableRemote(big)))
            .Should().Throw<InvalidOperationException>().WithMessage("*tope*");
    }

    [Fact]
    public void DumpCommand_guards_size_before_emitting_and_has_no_password()
    {
        var cmd = BackupExecutionRules.BuildDumpCommand("t", "u");
        cmd.Should().Contain("-gt " + BackupExecutionRules.MaxCompressedDumpBytes);
        cmd.Should().NotContain("PGPASSWORD").And.NotContain("-h ");   // socket local, sin password
        cmd.Should().Contain("} | gzip -c > \"$f.gz\"").And.NotContain("$f.sql");   // D2: directo al .gz
        cmd.Should().Contain("base64 -w 0").And.NotContain("| tr");              // D4
    }

    // T13: el dump NO puede cubrir una sola base. Falla si se vuelve a `pg_dump -d <una base fija>`.
    [Fact]
    public void DumpCommand_covers_every_non_template_database_and_roles_not_just_one()
    {
        var cmd = BackupExecutionRules.BuildDumpCommand("t", "postgres");
        cmd.Should().Contain("pg_dumpall -w -U 'postgres' --roles-only");
        cmd.Should().Contain("NOT datistemplate AND datallowconn");               // todas las bases no-plantilla
        cmd.Should().Contain("for db in $dbs; do");
        cmd.Should().Contain("pg_dump -w -U 'postgres' -d \"$db\"");              // un pg_dump POR base (variable de bucle)
        cmd.Should().NotContain("-d 'postgres'");                                  // no la base del admin fija
        cmd.Should().NotContain("--clean").And.NotContain("--no-owner").And.NotContain("--no-privileges"); // dueños y GRANT
        cmd.Should().Contain(BackupExecutionRules.RemoteDumpEnd);
        cmd.Should().Contain(BackupExecutionRules.RemoteDumpHeader);
    }

    // T13: cada base en SU transaccion; DROP/CREATE DATABASE FUERA de ella; la viva solo se toca tras cargar bien.
    [Fact]
    public void DumpCommand_loads_each_database_in_its_own_transaction_into_a_temp_db_then_swaps()
    {
        var cmd = BackupExecutionRules.BuildDumpCommand("t", "postgres");
        var iCreate = cmd.IndexOf("CREATE DATABASE aethra_restore_tmp", StringComparison.Ordinal);
        var iConnect = cmd.IndexOf("connect aethra_restore_tmp' 'BEGIN;'", StringComparison.Ordinal);
        var iDump = cmd.IndexOf("pg_dump -w", StringComparison.Ordinal);
        var iCommit = cmd.IndexOf("'COMMIT;' '\\connect template1'", StringComparison.Ordinal);
        var iDropLive = cmd.IndexOf("DROP DATABASE IF EXISTS \\\"$db\\\" WITH (FORCE)", StringComparison.Ordinal);
        var iRename = cmd.IndexOf("RENAME TO \\\"$db\\\"", StringComparison.Ordinal);
        var order = new[] { iCreate, iConnect, iDump, iCommit, iDropLive, iRename };
        order.Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
        cmd.Should().Contain("DROP DATABASE IF EXISTS aethra_restore_tmp WITH (FORCE)");
        cmd.Should().NotContain("--single-transaction");
        // roles: CREATE ROLE idempotente (el propio postgres ya existe)
        cmd.Should().Contain("IF NOT EXISTS (SELECT 1 FROM pg_roles");
    }

    [Fact]
    public void DumpCommand_busybox_safe_no_echo_with_backslashes_and_checks_every_stage()
    {
        var cmd = BackupExecutionRules.BuildDumpCommand("t", "postgres");
        cmd.Should().NotContain("echo '\\").And.NotContain("echo \"\\");          // echo interpreta \c en ash/dash
        cmd.Should().NotContain("[[").And.NotContain("<<<").And.NotContain("<(");  // nada que busybox ash no tenga
        // cada etapa que puede fallar registra su estado en el .rc (pipefail no es fiable)
        System.Text.RegularExpressions.Regex.Count(cmd, "echo 1 > \"\\$f\\.rc\"").Should().BeGreaterThanOrEqualTo(7);
        cmd.Should().Contain("{ pg_dump -w -U 'postgres' -d \"$db\" </dev/null || echo 1 > \"$f.rc\"; }");
        cmd.Should().Contain("{ pg_dumpall -w -U 'postgres' --roles-only </dev/null || echo 1 > \"$f.rc\"; }");
    }

    [Fact]
    public void RestoreApplyCommand_streams_without_sql_file_validates_gzip_first_and_is_not_one_transaction()
    {
        var cmd = BackupExecutionRules.BuildRestoreApplyCommand("t", "u");
        cmd.Should().Contain("gunzip -t").And.Contain("gunzip -c | psql -w -U 'u' -d template1");
        cmd.Should().Contain("ON_ERROR_STOP=1");
        // DROP/CREATE DATABASE no caben en una transaccion: NO --single-transaction (cada base lleva la suya)
        cmd.Should().NotContain("--single-transaction");
        cmd.Should().NotContain("$f.sql").And.NotContain("PGPASSWORD");
    }

    [Fact]
    public void RestoreCleanupCommand_removes_files_and_drops_the_temp_database_best_effort()
    {
        var cmd = BackupExecutionRules.BuildRestoreCleanupCommand("t", "u");
        cmd.Should().StartWith("rm -f /tmp/aethra-rs-t.b64");
        cmd.Should().Contain("DROP DATABASE IF EXISTS aethra_restore_tmp WITH (FORCE)").And.EndWith("|| true");
        cmd.Should().NotContain("PGPASSWORD");
    }

    [Fact]
    public void DecodeDumpOutput_rejects_truncated_gzip_and_incomplete_but_valid_base64()
    {
        var full = Gzip(Valid(RandomText(200_000)));
        BackupExecutionRules.DecodeDumpOutput(0, false, Convert.ToBase64String(full), "").Should().Equal(full);

        var noTrailer = full[..^8];                  // sin CRC+tamano: magia y cabecera intactas
        var half = full[..(full.Length / 2)];        // base64 decodificable pero dump incompleto
        foreach (var cut in new[] { noTrailer, half })
        {
            ((Action)(() => BackupExecutionRules.DecodeDumpOutput(0, false, Convert.ToBase64String(cut), "")))
                .Should().Throw<InvalidOperationException>().WithMessage("*incompleta o corrupta*");
        }
    }

    [Fact]
    public void DecodeDumpOutput_rejects_valid_gzip_without_remote_header()
        => ((Action)(() => BackupExecutionRules.DecodeDumpOutput(0, false, Convert.ToBase64String(Gzip("SELECT 1;\n")), "")))
            .Should().Throw<InvalidOperationException>().WithMessage("*remote v2*");

    [Fact]
    public void DecodeDumpOutput_reports_pg_dump_failure_marker()
        => ((Action)(() => BackupExecutionRules.DecodeDumpOutput(4, false, "", "aethra-dump-failed")))
            .Should().Throw<InvalidOperationException>().WithMessage("*pg_dump*error*");

    [Fact]
    public async Task Password_never_appears_in_any_generated_command_dump_restore_apply_cleanup()
    {
        var dumpRpc = new FakeRpc(_ => new ExecResult(0, Convert.ToBase64String(
            Gzip(Valid("SELECT 1;"))), "", false));
        await NewEngine(dumpRpc, Central).CreateBackupAsync(NewService(Bogota), CancellationToken.None);

        var restoreRpc = new FakeRpc(_ => new ExecResult(0, "", "", false));
        var backup = Gzip(Valid(RandomText(300_000)));
        await NewEngine(restoreRpc, Central).RestoreBackupAsync(NewService(Bogota), backup, CancellationToken.None);

        var all = dumpRpc.Calls.Concat(restoreRpc.Calls).Select(c => c.Command).ToList();
        all.Count.Should().BeGreaterThan(3);   // dump + trozos + apply + cleanup
        all.Should().OnlyContain(c => !c.Contains(Password) && !c.Contains("PGPASSWORD") && !c.Contains(" -W"));
    }

    // ---- cableado del engine (fallan si se revierte el branch local/remoto) ------------------------

    [Fact]
    public async Task CreateBackup_for_service_in_other_vm_dumps_inside_container_via_rpc()
    {
        var gz = Gzip(Valid("CREATE TABLE t();"));
        var rpc = new FakeRpc(_ => new ExecResult(0, Convert.ToBase64String(gz), "", false));
        var engine = NewEngine(rpc, Central);

        var bytes = await engine.CreateBackupAsync(NewService(Bogota), CancellationToken.None);

        bytes.Should().Equal(gz);
        rpc.Calls.Should().ContainSingle();
        rpc.Calls[0].VmId.Should().Be(Bogota);
        rpc.Calls[0].Container.Should().Be("pg-iot-prod");
        rpc.Calls[0].Command.Should().Contain("pg_dump");
    }

    [Fact]
    public async Task CreateBackup_oversize_in_satellite_fails_explicitly()
    {
        var rpc = new FakeRpc(_ => new ExecResult(3, "", "aethra-dump-too-large:50000000", false));
        var engine = NewEngine(rpc, Central);

        var act = () => engine.CreateBackupAsync(NewService(Bogota), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no se trunca*");
    }

    [Theory]
    [InlineData(Central)]   // misma VM que el central
    [InlineData(null)]      // central desconocido => comportamiento previo a T10
    public async Task CreateBackup_for_central_vm_or_unknown_central_never_uses_rpc(string? centralVmId)
    {
        var rpc = new FakeRpc(_ => new ExecResult(0, "", "", false));
        var engine = NewEngine(rpc, centralVmId);
        var svc = NewService(centralVmId ?? Bogota, port: 1); // 127.0.0.1:1 → Npgsql falla rápido

        var act = () => engine.CreateBackupAsync(svc, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();           // camino Npgsql (sin Postgres en :1)
        rpc.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_for_service_in_other_vm_uploads_chunks_applies_psql_and_cleans_up()
    {
        var sql = Valid(RandomText(300_000));
        var backup = Gzip(sql);
        var rpc = new FakeRpc(_ => new ExecResult(0, "", "", false));
        var engine = NewEngine(rpc, Central);

        await engine.RestoreBackupAsync(NewService(Bogota), backup, CancellationToken.None);

        rpc.Calls.Should().OnlyContain(c => c.VmId == Bogota && c.Command.Length < 128 * 1024);
        var appends = rpc.Calls.Where(c => c.Command.Contains("printf %s")).ToList();
        appends[0].Command.Should().Contain(" > \"$f.b64\"");
        appends.Skip(1).Should().OnlyContain(c => c.Command.Contains(">> \"$f.b64\""));
        var rebuilt = string.Concat(appends.Select(c => c.Command.Split('\'')[1]));
        rebuilt.Should().Be(Convert.ToBase64String(backup));
        rpc.Calls.Should().Contain(c => c.Command.Contains("psql") && c.Command.Contains("ON_ERROR_STOP=1"));
        rpc.Calls[^1].Command.Should().StartWith("rm -f /tmp/aethra-rs-");
    }

    [Fact]
    public async Task Restore_failure_in_psql_throws_and_still_cleans_up()
    {
        var rpc = new FakeRpc(cmd => cmd.Contains("psql")
            ? new ExecResult(3, "", "syntax error", false)
            : new ExecResult(0, "", "", false));
        var engine = NewEngine(rpc, Central);
        var backup = Gzip(Valid("SELECT 1;"));

        var act = () => engine.RestoreBackupAsync(NewService(Bogota), backup, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*psql*syntax error*");
        rpc.Calls[^1].Command.Should().StartWith("rm -f /tmp/aethra-rs-");
    }

    [Fact]
    public async Task Restore_of_local_format_backup_in_remote_service_is_rejected_without_exec()
    {
        var rpc = new FakeRpc(_ => new ExecResult(0, "", "", false));
        var engine = NewEngine(rpc, Central);

        var act = () => engine.RestoreBackupAsync(
            NewService(Bogota), Gzip("-- aethra-pg-dump v1\nCOPY a FROM STDIN;\n\\.\n"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        rpc.Calls.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurationCentralVmIdProvider_reads_key_and_defaults_to_null()
    {
        var empty = new ConfigurationBuilder().Build();
        new ConfigurationCentralVmIdProvider(empty).GetCentralVmId().Should().BeNull();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Services:CentralVmId"] = " vm_x " }).Build();
        new ConfigurationCentralVmIdProvider(cfg).GetCentralVmId().Should().Be("vm_x");
    }

    // ---- el comando generado, EJECUTADO en un sh real con psql/pg_dump/pg_dumpall simulados ------------

    [Fact]
    public void DumpCommand_executed_in_a_real_sh_emits_every_database_and_the_roles()
    {
        using var sandbox = ShellSandbox.TryCreate();
        if (sandbox is null) { return; }   // sin sh en el PATH: lo cubren los tests de texto

        var run = sandbox.Run(BackupExecutionRules.BuildDumpCommand("t13a", "postgres"), failDb: null);

        run.ExitCode.Should().Be(0, run.Stderr);
        var script = Gunzip(Convert.FromBase64String(run.Stdout.Trim()));
        script.Should().StartWith(BackupExecutionRules.RemoteDumpHeader + "\n");
        script.TrimEnd().Should().EndWith(BackupExecutionRules.RemoteDumpEnd);
        script.Should().Contain("-- aethra-section database inonetickets").And.Contain("-- aethra-section database postgres");
        script.Should().Contain("-- data of inonetickets").And.Contain("-- data of postgres");   // un pg_dump por base
        script.Should().Contain("DO $q$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE quote_ident(rolname) = $n$inonetickets$n$)");
        script.Should().Contain("CREATE ROLE inonetickets; END IF; END $q$;").And.Contain("ALTER ROLE inonetickets WITH LOGIN;");
        script.Should().Contain("\\connect aethra_restore_tmp\nBEGIN;\n-- data of inonetickets\nCOMMIT;\n\\connect template1\n");
        script.Should().Contain("ALTER DATABASE aethra_restore_tmp RENAME TO \"inonetickets\";");
        BackupExecutionRules.EnsureRestorableRemote(Convert.FromBase64String(run.Stdout.Trim()));
    }

    [Fact]
    public void DumpCommand_executed_in_a_real_sh_fails_explicitly_when_any_database_dump_fails()
    {
        using var sandbox = ShellSandbox.TryCreate();
        if (sandbox is null) { return; }

        var run = sandbox.Run(BackupExecutionRules.BuildDumpCommand("t13b", "postgres"), failDb: "postgres");

        run.ExitCode.Should().Be(4);
        run.Stderr.Should().Contain(BackupExecutionRules.DumpFailedMarker);
        run.Stdout.Should().BeEmpty();   // ningun backup "verde" con una base sin volcar
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static string Valid(string body)
        => BackupExecutionRules.RemoteDumpHeader + "\n" + body + "\n" + BackupExecutionRules.RemoteDumpEnd + "\n";

    private static string Gunzip(byte[] gz)
    {
        using var gzs = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var r = new StreamReader(gzs, new UTF8Encoding(false));
        return r.ReadToEnd();
    }

    /// <summary>sh real + psql/pg_dump/pg_dumpall falsos en un directorio temporal (null si no hay sh).</summary>
    private sealed class ShellSandbox : IDisposable
    {
        private const string FakePsql = """
            #!/bin/sh
            while [ $# -gt 0 ]; do if [ "$1" = -c ]; then q="$2"; fi; shift; done
            case "$q" in
              *'count(*)'*) echo 0;;
              *'SELECT datname'*) printf 'inonetickets\npostgres\n';;
              *'CREATE DATABASE'*) echo 'CREATE DATABASE aethra_restore_tmp OWNER x;';;
            esac
            """;

        private const string FakePgDump = """
            #!/bin/sh
            while [ $# -gt 0 ]; do if [ "$1" = -d ]; then db="$2"; fi; shift; done
            echo "-- data of $db"
            [ "$db" = "$FAIL_DB" ] && exit 1
            exit 0
            """;

        private const string FakePgDumpall = """
            #!/bin/sh
            printf 'CREATE ROLE inonetickets;\nALTER ROLE inonetickets WITH LOGIN;\n'
            """;

        private readonly string _dir;
        private readonly string _sh;

        private ShellSandbox(string dir, string sh) { _dir = dir; _sh = sh; }

        public static ShellSandbox? TryCreate()
        {
            var exe = OperatingSystem.IsWindows() ? "sh.exe" : "sh";
            var sh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
            if (sh is null) { return null; }
            var dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "aethra-t13-" + Guid.NewGuid().ToString("N"))).FullName;
            foreach (var (name, body) in new[] { ("psql", FakePsql), ("pg_dump", FakePgDump), ("pg_dumpall", FakePgDumpall) })
            {
                File.WriteAllText(Path.Combine(dir, name), body.Replace("\r\n", "\n") + "\n");   // LF: sh no tolera CR
            }
            return new ShellSandbox(dir, sh);
        }

        public (int ExitCode, string Stdout, string Stderr) Run(string command, string? failDb)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_sh)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
            psi.Environment["PATH"] = _dir + Path.PathSeparator + Path.GetDirectoryName(_sh)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["FAIL_DB"] = failDb ?? "-none-";
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardInput.Close();
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, o, err.Result);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }

    private static PostgresBackupEngine NewEngine(FakeRpc rpc, string? centralVmId)
        => new(new StaticCodec(), new StaticHost(), rpc, new StaticCentral(centralVmId),
            NullLogger<PostgresBackupEngine>.Instance);

    private static ManagedService NewService(string vmId, int? port = null)
        => ManagedService.Adopt("pg-iot-prod", "pg", ServiceType.Postgres, "16", vmId, "pg-iot-prod",
            "postgres:16", port ?? 5432, "aethra-net", [1], DateTimeOffset.UtcNow);

    private static string RandomText(int n)
    {
        var b = new byte[n / 2];
        new Random(42).NextBytes(b);
        return Convert.ToHexString(b);
    }

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(Encoding.UTF8.GetBytes(text));
        }
        return ms.ToArray();
    }

    private sealed class StaticCentral(string? id) : ICentralVmIdProvider
    {
        public string? GetCentralVmId() => id;
    }

    private sealed class StaticCodec : IAdminCredentialsCodec
    {
        public byte[] Encode(AdminCredentials credentials) => [1];
        public AdminCredentials Decode(byte[] cipher) => new("admin", Password);
    }

    private sealed class StaticHost : IManagedServiceHostResolver
    {
        public Task<string> ResolveAsync(ManagedService service, CancellationToken ct) => Task.FromResult("127.0.0.1");
        public Task<int> ResolveManagementPortAsync(ManagedService service, CancellationToken ct) => Task.FromResult(service.InternalPort);
    }

    private sealed class FakeRpc(Func<string, ExecResult> onExec) : ISatelliteRpcClient
    {
        public List<(string VmId, string Container, string Command)> Calls { get; } = [];

        public Task<ExecResult> SendExecAsync(string vmId, string container, string command, int timeoutSeconds, CancellationToken ct)
        {
            Calls.Add((vmId, container, command));
            return Task.FromResult(onExec(command));
        }

        public Task<BuildResult> SendBuildAsync(string vmId, BuildSpec spec, RegistryAuth? pushTo, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunResult> SendRunAsync(string vmId, RunSpec spec, RegistryAuth? pullFrom, CancellationToken ct) => throw new NotSupportedException();
        public Task SendStopAsync(string vmId, string containerNameOrId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendRestartAsync(string vmId, string containerNameOrId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendRemoveAsync(string vmId, string containerNameOrId, bool force, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<LogChunk> StreamLogsAsync(string vmId, string containerNameOrId, int tailLines, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ContainerInfo>> SendListContainersAsync(string vmId, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoreFileResponse> SendStoreFileAsync(string vmId, string relativePath, byte[] content, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> SendReadFileAsync(string vmId, string relativePath, CancellationToken ct) => throw new NotSupportedException();
        public Task SendDeleteFileAsync(string vmId, string relativePath, CancellationToken ct) => throw new NotSupportedException();
    }
}
