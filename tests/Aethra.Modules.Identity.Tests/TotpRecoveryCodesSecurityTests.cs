using System.Security.Cryptography;
using System.Text;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// Pruebas de seguridad de Issue #91 (Authoritt/Aethra):
/// Almacenamiento de recovery codes TOTP como hashes one-way (Argon2id) en lugar de ciphertext reversible.
/// Cubre cada criterio de aceptación del contrato con PostgreSQL 18 real:
/// - Material persistido no puede convertirse a los códigos originales mediante llaves de descifrado.
/// - Comportamiento de login sigue siendo de un solo uso y compatible con formato/mayúsculas.
/// - Códigos en texto plano solo se devuelven en enroll/regenerate.
/// - Máscara de uso/no uso sigue siendo estricta.
/// - Migración perezosa de formato legacy a hashes one-way al usarse.
/// - Backfill idempotente y transaccional.
/// </summary>
public sealed class TotpRecoveryCodesSecurityTests(PostgreSql18Fixture postgres)
    : IClassFixture<PostgreSql18Fixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] TotpSeed = "12345678901234567890"u8.ToArray();

    [Fact]
    public async Task AC1_AC3_AC5_Enrollment_persists_one_way_hashes_and_never_stores_plaintext_codes()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var user = await CreateEnrolledUserAsync(db, codec, clock);

        var handler = new VerifyTotpEnrollmentHandler(db, codec, clock);
        var currentTotpCode = TotpGenerator.Generate(TotpSeed);

        // AC3: Plaintext codes are returned ONLY during initial generation/regeneration
        var result = await handler.Handle(new VerifyTotpEnrollmentCommand(user.Id.ToString(), currentTotpCode), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.RecoveryCodes.Should().HaveCount(RecoveryCodes.CodeCount);

        var reloadedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloadedUser.TotpEnabled.Should().BeTrue();
        reloadedUser.TotpRecoveryCodesCipher.Should().NotBeNull();

        // AC1 & AC5: Persisted material is in the hashed format and does NOT contain plaintext codes
        RecoveryCodes.IsHashed(reloadedUser.TotpRecoveryCodesCipher).Should().BeTrue();

        var rawPersistedText = Encoding.UTF8.GetString(reloadedUser.TotpRecoveryCodesCipher!);
        foreach (var code in result.Value.RecoveryCodes)
        {
            rawPersistedText.Should().NotContain(code);
        }

        // AC1: DataProtection decryption keys cannot convert the hashed material back to original plaintext codes
        var decryptAttempt = () => codec.Unprotect(reloadedUser.TotpRecoveryCodesCipher!);
        decryptAttempt.Should().Throw<CryptographicException>();

        var storedHashes = RecoveryCodes.UnpackHashes(reloadedUser.TotpRecoveryCodesCipher!);
        storedHashes.Should().HaveCount(RecoveryCodes.CodeCount);
        storedHashes.Should().OnlyContain(h => h.StartsWith("$argon2id$v=19$m=65536,t=4,p=2$", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC1_AC3_Regeneration_rotates_and_persists_new_one_way_hashes()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var user = await CreateEnrolledUserAsync(db, codec, clock);

        var enrollHandler = new VerifyTotpEnrollmentHandler(db, codec, clock);
        var initialTotpCode = TotpGenerator.Generate(TotpSeed);
        var enrollResult = await enrollHandler.Handle(new VerifyTotpEnrollmentCommand(user.Id.ToString(), initialTotpCode), CancellationToken.None);
        var oldCodes = enrollResult.Value.RecoveryCodes;

        // Regenerate codes using a valid TOTP code
        var regenHandler = new RegenerateRecoveryCodesHandler(db, codec, clock);
        var regenTotpCode = TotpGenerator.Generate(TotpSeed);
        var regenResult = await regenHandler.Handle(new RegenerateRecoveryCodesCommand(user.Id.ToString(), regenTotpCode), CancellationToken.None);

        regenResult.IsSuccess.Should().BeTrue();
        var newCodes = regenResult.Value.RecoveryCodes;
        newCodes.Should().NotBeEquivalentTo(oldCodes);

        var reloadedUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        RecoveryCodes.IsHashed(reloadedUser.TotpRecoveryCodesCipher).Should().BeTrue();

        // Old codes no longer authenticate
        (await TotpVerifier.VerifyAsync(reloadedUser, oldCodes[0], codec, db, clock, CancellationToken.None))
            .Should().BeFalse();

        // New codes authenticate
        (await TotpVerifier.VerifyAsync(reloadedUser, newCodes[0], codec, db, clock, CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AC2_AC4_AC5_Hashed_recovery_code_authenticates_and_enforces_single_use()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var codes = RecoveryCodes.Generate();
        var user = await CreateUserWithHashedCodesAsync(db, codec, clock, codes);

        // First use of code 0 succeeds
        var firstAttempt = await TotpVerifier.VerifyAsync(user, codes[0], codec, db, clock, CancellationToken.None);
        firstAttempt.Should().BeTrue();

        // AC2 & AC4: Single-use enforcement — replay of the exact same code fails
        var secondAttempt = await TotpVerifier.VerifyAsync(user, codes[0], codec, db, clock, CancellationToken.None);
        secondAttempt.Should().BeFalse();

        // Used mask bit 0 is set (1 << 0 = 1)
        user.TotpRecoveryCodesUsedMask.Should().Be(1);
        RecoveryCodes.RemainingCount(user.TotpRecoveryCodesUsedMask).Should().Be(9);
        RecoveryCodes.FormatRemainingFraction(user.TotpRecoveryCodesUsedMask).Should().Be("9/10");

        // Code 1 is still unused and succeeds
        var code1Attempt = await TotpVerifier.VerifyAsync(user, codes[1], codec, db, clock, CancellationToken.None);
        code1Attempt.Should().BeTrue();

        user.TotpRecoveryCodesUsedMask.Should().Be(3); // bits 0 and 1 set
        RecoveryCodes.RemainingCount(user.TotpRecoveryCodesUsedMask).Should().Be(8);
    }

    [Fact]
    public async Task AC2_Hashed_recovery_code_is_case_insensitive_and_whitespace_tolerant()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var codes = new[] { "ABCD2345", "EFGH6789" };
        var user = await CreateUserWithHashedCodesAsync(db, codec, clock, codes);

        // Lowercase with leading and trailing spaces
        var authenticated = await TotpVerifier.VerifyAsync(user, "  abcd2345  ", codec, db, clock, CancellationToken.None);
        authenticated.Should().BeTrue();

        user.TotpRecoveryCodesUsedMask.Should().Be(1);
    }

    [Fact]
    public async Task AC2_AC5_Invalid_and_malformed_recovery_codes_are_rejected()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var codes = new[] { "ABCD2345" };
        var user = await CreateUserWithHashedCodesAsync(db, codec, clock, codes);

        // Wrong code
        (await TotpVerifier.VerifyAsync(user, "WXYZ2345", codec, db, clock, CancellationToken.None)).Should().BeFalse();

        // Too short / too long
        (await TotpVerifier.VerifyAsync(user, "ABCD234", codec, db, clock, CancellationToken.None)).Should().BeFalse();
        (await TotpVerifier.VerifyAsync(user, "ABCD23456", codec, db, clock, CancellationToken.None)).Should().BeFalse();

        // Characters outside reduced base32 alphabet ('1', '0', 'O', 'I')
        (await TotpVerifier.VerifyAsync(user, "12345678", codec, db, clock, CancellationToken.None)).Should().BeFalse();
        (await TotpVerifier.VerifyAsync(user, "ABCD234O", codec, db, clock, CancellationToken.None)).Should().BeFalse();

        // Empty / null
        (await TotpVerifier.VerifyAsync(user, "", codec, db, clock, CancellationToken.None)).Should().BeFalse();
        (await TotpVerifier.VerifyAsync(user, null, codec, db, clock, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task AC2_Legacy_encrypted_codes_authenticate_and_migrate_lazily_to_one_way_hashes()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var codes = new[] { "ABCD2345", "EFGH6789", "JKLM2345" };

        // Simulate a legacy production user with DataProtection-encrypted plaintext bundle
        var legacyPacked = RecoveryCodes.Pack(codes);
        var legacyCipher = codec.Protect(legacyPacked);

        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Legacy User", Now);
        user.BeginTotpEnrollment(codec.Protect(TotpSeed), Now);
        user.CompleteTotpEnrollment(legacyCipher, Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Before login, payload is NOT hashed
        RecoveryCodes.IsHashed(user.TotpRecoveryCodesCipher).Should().BeFalse();

        // First login with legacy recovery code
        var verified = await TotpVerifier.VerifyAsync(user, "abcd2345", codec, db, clock, CancellationToken.None);
        verified.Should().BeTrue();

        // User is now lazily migrated to one-way hashes!
        RecoveryCodes.IsHashed(user.TotpRecoveryCodesCipher).Should().BeTrue();
        user.TotpRecoveryCodesUsedMask.Should().Be(1);

        // The consumed code cannot be reused
        (await TotpVerifier.VerifyAsync(user, "ABCD2345", codec, db, clock, CancellationToken.None))
            .Should().BeFalse();

        // Remaining code 1 authenticates successfully against the newly migrated hashed bundle
        (await TotpVerifier.VerifyAsync(user, "EFGH6789", codec, db, clock, CancellationToken.None))
            .Should().BeTrue();

        user.TotpRecoveryCodesUsedMask.Should().Be(3);
    }

    [Fact]
    public async Task Backfill_idempotently_converts_legacy_users_to_one_way_hashes()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        var codesLegacy = new[] { "ABCD2345", "EFGH6789" };
        var codesHashed = new[] { "JKLM2345", "NPQR6789" };

        // User 1: Legacy encrypted codes
        var user1 = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Legacy 1", Now);
        user1.BeginTotpEnrollment(codec.Protect(TotpSeed), Now);
        user1.CompleteTotpEnrollment(codec.Protect(RecoveryCodes.Pack(codesLegacy)), Now);
        db.Users.Add(user1);

        // User 2: Already hashed codes
        var user2 = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Hashed 2", Now);
        user2.BeginTotpEnrollment(codec.Protect(TotpSeed), Now);
        user2.CompleteTotpEnrollment(RecoveryCodes.Hash(codesHashed), Now);
        db.Users.Add(user2);

        await db.SaveChangesAsync();

        RecoveryCodes.IsHashed(user1.TotpRecoveryCodesCipher).Should().BeFalse();
        RecoveryCodes.IsHashed(user2.TotpRecoveryCodesCipher).Should().BeTrue();

        // Run backfill
        var migrated = await RecoveryCodesHashBackfill.BackfillAsync(db, codec, NullLogger.Instance);
        migrated.Should().Be(1);

        // Both are now hashed in DB
        var reloadedUser1 = await db.Users.SingleAsync(u => u.Id == user1.Id);
        var reloadedUser2 = await db.Users.SingleAsync(u => u.Id == user2.Id);
        RecoveryCodes.IsHashed(reloadedUser1.TotpRecoveryCodesCipher).Should().BeTrue();
        RecoveryCodes.IsHashed(reloadedUser2.TotpRecoveryCodesCipher).Should().BeTrue();

        // Idempotency: running backfill again finds 0 users needing migration
        var secondRunMigrated = await RecoveryCodesHashBackfill.BackfillAsync(db, codec, NullLogger.Instance);
        secondRunMigrated.Should().Be(0);

        // Migrated user 1 authenticates with their recovery code
        (await TotpVerifier.VerifyAsync(reloadedUser1, "ABCD2345", codec, db, clock, CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_leaves_undecryptable_legacy_codes_untouched_so_they_still_work()
    {
        await using var db = await CreateDatabaseAsync();
        var (codec, clock) = CreateHelpers();
        // Otro anillo de claves: Unprotect lanza CryptographicException, como un key ring ausente o rotado.
        var (foreignCodec, _) = CreateHelpers();
        var codes = new[] { "ABCD2345", "EFGH6789" };

        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Legacy undecryptable", Now);
        user.BeginTotpEnrollment(codec.Protect(TotpSeed), Now);
        user.CompleteTotpEnrollment(codec.Protect(RecoveryCodes.Pack(codes)), Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var original = user.TotpRecoveryCodesCipher!.ToArray();

        var migrated = await RecoveryCodesHashBackfill.BackfillAsync(db, foreignCodec, NullLogger.Instance);

        migrated.Should().Be(0);
        var reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        reloaded.TotpRecoveryCodesCipher.Should().Equal(original,
            "un payload que no se puede descifrar no debe sustituirse por el hash de su ciphertext");
        (await TotpVerifier.VerifyAsync(reloaded, "ABCD2345", codec, db, clock, CancellationToken.None))
            .Should().BeTrue();
    }

    private async Task<IdentityDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static (ITotpSecretCodec codec, IClock clock) CreateHelpers()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var codec = new DataProtectionTotpSecretCodec(dataProtectionProvider);
        var clock = new FixedClock(Now);
        return (codec, clock);
    }

    private static async Task<User> CreateEnrolledUserAsync(IdentityDbContext db, ITotpSecretCodec codec, IClock clock)
    {
        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Test User", clock.UtcNow);
        user.BeginTotpEnrollment(codec.Protect(TotpSeed), clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<User> CreateUserWithHashedCodesAsync(
        IdentityDbContext db, ITotpSecretCodec codec, IClock clock, IReadOnlyList<string> codes)
    {
        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], "Test User", clock.UtcNow);
        user.BeginTotpEnrollment(codec.Protect(TotpSeed), clock.UtcNow);
        user.CompleteTotpEnrollment(RecoveryCodes.Hash(codes), clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
