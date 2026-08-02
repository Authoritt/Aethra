using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Shared.Contracts.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// Las policies que registra <see cref="ApiKeyAuthorizationExtensions.AddApiKeyScopePolicies"/>:
/// el punto único donde se responde "¿este principal puede hacer esto?" para todo endpoint con
/// scope y las 117 tools MCP.
///
/// <para>
/// <b>Por qué esto no lo cubría <c>IdentityScopeTests</c></b>: aquel prueba
/// <c>ApiKey.HasScope</c> y <c>Role.HasScope</c>, que operan sobre la ENTIDAD. Estas policies
/// operan sobre un <see cref="ClaimsPrincipal"/> y tienen dos vías de concesión que la entidad
/// no tiene — el claim <c>scope</c> y el role <c>admin</c> — y esa segunda vía concede sin
/// que exista ningún scope. Son caminos de código distintos.
/// </para>
///
/// <para>
/// Se evalúa la <see cref="AssertionRequirement"/> realmente registrada, no una reimplementación
/// de la regla: si el registro cambia, estas pruebas lo ven. Cubren especialmente los casos
/// NEGATIVOS, porque los positivos fallan ruidosamente en la app y los negativos fallan en
/// abierto y en silencio.
/// </para>
/// </summary>
public sealed class ApiKeyAuthorizationPolicyTests
{
    private const string UnScope = "proxy:read";

    /// <summary>Todos los scopes reales del catálogo salvo el wildcard, que no genera policy.</summary>
    public static TheoryData<string> ScopesConPolicy()
    {
        var data = new TheoryData<string>();
        foreach (var s in ApiKey.AllScopes.Where(s => s != ApiKey.AdminScope))
        {
            data.Add(s);
        }
        return data;
    }

    private static AuthorizationPolicy Policy(string scope)
    {
        var options = new AuthorizationOptions();
        options.AddApiKeyScopePolicies();
        var policy = options.GetPolicy(ApiKeyAuthorizationExtensions.PolicyName(scope));
        policy.Should().NotBeNull($"el catálogo debería registrar una policy para '{scope}'");
        return policy!;
    }

    private static async Task<bool> Concede(string scope, ClaimsPrincipal principal)
    {
        var policy = Policy(scope);
        var requirement = policy.Requirements.OfType<AssertionRequirement>().Single();
        var ctx = new AuthorizationHandlerContext(policy.Requirements, principal, resource: null);
        await requirement.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    /// <summary>Autenticado: pasar un authenticationType es lo que pone IsAuthenticated en true.</summary>
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static ClaimsPrincipal Anonimo(params Claim[] claims)
        => new(new ClaimsIdentity(claims));   // sin authenticationType => IsAuthenticated == false

    private static Claim Scope(string value) => new(ApiKeyAuthSchemes.ScopeClaim, value);

    private static Claim RoleAdmin() => new(ClaimTypes.Role, Role.AdminSlug);

    // ---------- registro del catálogo ----------

    [Theory]
    [MemberData(nameof(ScopesConPolicy))]
    public void Cada_scope_del_catalogo_registra_su_policy(string scope)
    {
        Policy(scope).Requirements.Should().ContainSingle();
    }

    /// <summary>
    /// El loop hace <c>continue</c> sobre <see cref="ApiKey.AdminScope"/>: NO existe una policy
    /// <c>scope:*</c>. Es deliberado — el wildcard se acepta como CONCESIÓN, no se exige como
    /// requisito. Nada más lo afirmaba, así que un `continue` borrado por accidente pasaba
    /// desapercibido.
    /// </summary>
    [Fact]
    public void El_wildcard_no_genera_una_policy_propia()
    {
        var options = new AuthorizationOptions();
        options.AddApiKeyScopePolicies();

        options.GetPolicy(ApiKeyAuthorizationExtensions.PolicyName(ApiKey.AdminScope))
            .Should().BeNull();
    }

    // ---------- concesiones ----------

    [Theory]
    [MemberData(nameof(ScopesConPolicy))]
    public async Task El_scope_exacto_concede(string scope)
    {
        (await Concede(scope, Principal(Scope(scope)))).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ScopesConPolicy))]
    public async Task El_wildcard_concede_cualquier_scope(string scope)
    {
        (await Concede(scope, Principal(Scope(ApiKey.AdminScope)))).Should().BeTrue();
    }

    /// <summary>
    /// El bypass que no se ve en la lista de scopes de una key: un principal con role=admin
    /// y CERO claims de scope satisface todas las policies. Es intencional (el bootstrap emite
    /// role=admin sin scope=*), y es exactamente lo que el issue #2 tendrá que respetar si
    /// introduce denies — un deny evaluado solo en la vía de scopes dejaría esta abierta.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScopesConPolicy))]
    public async Task El_role_admin_concede_sin_ningun_scope(string scope)
    {
        (await Concede(scope, Principal(RoleAdmin()))).Should().BeTrue();
    }

    // ---------- denegaciones (las que fallan en abierto) ----------

    [Fact]
    public async Task Un_scope_distinto_no_concede()
    {
        (await Concede(UnScope, Principal(Scope("vms:write")))).Should().BeFalse();
    }

    [Fact]
    public async Task Sin_claims_no_concede()
    {
        (await Concede(UnScope, Principal())).Should().BeFalse();
    }

    /// <summary>
    /// No hay jerarquía ni prefijos: <c>proxy:*</c> y <c>proxy</c> no conceden <c>proxy:read</c>.
    /// El único comodín es <c>*</c> exacto.
    /// </summary>
    [Theory]
    [InlineData("proxy")]
    [InlineData("proxy:*")]
    [InlineData("proxy:read:extra")]
    [InlineData("PROXY:READ")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task No_hay_emparejamiento_por_prefijo_ni_por_jerarquia(string claimValue)
    {
        (await Concede(UnScope, Principal(Scope(claimValue)))).Should().BeFalse();
    }

    /// <summary>
    /// <c>HasAdminRoleClaim</c> corta temprano si el identity no está autenticado. Sin ese
    /// corte, un principal fabricado con role=admin pero sin autenticar concedería todo.
    /// </summary>
    [Fact]
    public async Task El_role_admin_sin_autenticar_no_concede()
    {
        (await Concede(UnScope, Anonimo(RoleAdmin()))).Should().BeFalse();
    }

    /// <summary>
    /// El claim de scope SÍ concede aunque el identity no esté autenticado, porque
    /// <c>HasScopeClaim</c> no comprueba <c>IsAuthenticated</c>. Hoy es inalcanzable: los claims
    /// de scope solo los emite el handler de auth tras validar la key. Se fija aquí para que si
    /// algún día otra vía inyecta claims sin autenticar, el cambio de comportamiento se vea en
    /// esta prueba en vez de en producción.
    /// </summary>
    [Fact]
    public async Task El_claim_de_scope_no_exige_estar_autenticado()
    {
        (await Concede(UnScope, Anonimo(Scope(UnScope)))).Should().BeTrue();
    }

    [Fact]
    public async Task Un_role_que_no_es_admin_no_concede()
    {
        (await Concede(UnScope, Principal(new Claim(ClaimTypes.Role, "operador")))).Should().BeFalse();
    }
}
