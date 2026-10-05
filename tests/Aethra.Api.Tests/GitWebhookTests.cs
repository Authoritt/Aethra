using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aethra.Shared.Contracts.Projects;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// OT-012 (H3 de OT-011) contra el endpoint real <c>POST /webhooks/git</c>: sin oraculo de
/// Templates (401 uniforme) y sin replay de un cuerpo firmado.
/// </summary>
public sealed class GitWebhookTests(GitWebhookTests.Factory factory) : IClassFixture<GitWebhookTests.Factory>
{
    private const string Secret = "s3cret-ot012";
    private const string KnownRepo = "https://github.com/acme/known.git";
    private const string TwinRepo = "https://github.com/acme/twin.git";
    private const string SecretB = "otro-secreto-ot012";

    public sealed class Factory : AethraApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var lookup = Substitute.For<ITemplateLookup>();
                lookup.FindByRepoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(ci => Task.FromResult<IReadOnlyList<TemplateForBuildView>>(
                        ci.ArgAt<string>(0) switch
                        {
                            KnownRepo => [Template()],
                            // Dos Templates del mismo repo+rama, cada uno con su webhook y su secreto.
                            TwinRepo => [Template("tpl_twin_a", TwinRepo, Secret), Template("tpl_twin_b", TwinRepo, SecretB)],
                            _ => [],
                        }));
                services.RemoveAll<ITemplateLookup>();
                services.AddSingleton(lookup);
            });
        }

        // WatchPaths que nunca casa: la entrega autentica se acepta y no dispara builds (sin BD).
        private static TemplateForBuildView Template(
            string id = "tpl_1", string repo = KnownRepo, string secret = Secret) => new(
            TemplateId: id, ProjectId: "prj_1", Slug: id, Name: id,
            GitRepoUrl: repo, Branch: "main", WebhookSecret: secret, BaseDirectory: ".",
            WatchPaths: ["never/**"], BuildType: "dockerfile", DockerfilePath: "Dockerfile");
    }

    private static byte[] PushBody(string repo, string sha) => Encoding.UTF8.GetBytes(
        $$$"""{"ref":"refs/heads/main","after":"{{{sha}}}","commits":[],"repository":{"clone_url":"{{{repo}}}"}}""");

    private static string Sign(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
    }

    private async Task<(HttpStatusCode Status, string Body)> PostAsync(byte[] body, string? signature, string delivery)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/git")
        {
            Content = new ByteArrayContent(body),
        };
        req.Content.Headers.ContentType = new("application/json");
        req.Headers.Host = AethraApiFactory.PanelHost;
        req.Headers.Add("X-GitHub-Event", "push");
        req.Headers.Add("X-GitHub-Delivery", delivery);
        if (signature is not null)
        {
            req.Headers.Add("X-Hub-Signature-256", signature);
        }
        using var res = await client.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_repo_and_bad_signature_get_the_same_answer()
    {
        var unknown = await PostAsync(PushBody("https://github.com/acme/unknown.git", "a1"), "sha256=00", "d-u");
        var badSig = await PostAsync(PushBody(KnownRepo, "a2"), "sha256=00", "d-b");

        unknown.Status.Should().Be(HttpStatusCode.Unauthorized, "antes: 200 {matched_templates:0}, un oraculo");
        badSig.Status.Should().Be(HttpStatusCode.Unauthorized);
        // Mismo cuerpo salvo el traceId (unico por peticion).
        static string SinTrace(string b) => System.Text.RegularExpressions.Regex.Replace(b, "\"traceId\":\"[^\"]*\"", string.Empty);
        SinTrace(unknown.Body).Should().Be(SinTrace(badSig.Body));
    }

    [Fact]
    public async Task Signed_delivery_is_accepted_once_and_its_replay_is_rejected()
    {
        var body = PushBody(KnownRepo, "b1");
        var sig = Sign(body, Secret);

        (await PostAsync(body, sig, "d-1")).Status.Should().Be(HttpStatusCode.OK);
        (await PostAsync(body, sig, "d-1")).Status.Should().Be(HttpStatusCode.Conflict, "mismo X-GitHub-Delivery");
        (await PostAsync(body, sig, "d-1-forged")).Status.Should().Be(HttpStatusCode.Conflict,
            "la cabecera de entrega no esta firmada: cambiarla no convierte el replay en entrega nueva");
    }

    [Fact]
    public async Task A_new_push_with_a_new_body_is_accepted()
    {
        var first = PushBody(KnownRepo, "c1");
        var second = PushBody(KnownRepo, "c2");

        (await PostAsync(first, Sign(first, Secret), "d-c1")).Status.Should().Be(HttpStatusCode.OK);
        (await PostAsync(second, Sign(second, Secret), "d-c2")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unauthenticated_post_cannot_burn_a_real_delivery_id()
    {
        // El registro se hace solo tras validar la firma: un anonimo que adivine/copie el id de
        // una entrega no puede hacer que la legitima se rechace como replay.
        var body = PushBody(KnownRepo, "e1");
        (await PostAsync(body, "sha256=00", "d-e1")).Status.Should().Be(HttpStatusCode.Unauthorized);

        (await PostAsync(body, Sign(body, Secret), "d-e1")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Two_templates_on_the_same_repo_and_branch_both_get_their_delivery()
    {
        // G2 de OT-012 (ALTO): GitHub manda el MISMO cuerpo por los dos webhooks (distinto secreto
        // y distinto X-GitHub-Delivery). Con la clave global por cuerpo, el segundo recibia 409.
        var body = PushBody(TwinRepo, "f1");

        (await PostAsync(body, Sign(body, Secret), "d-twin-a")).Status.Should().Be(HttpStatusCode.OK);
        (await PostAsync(body, Sign(body, SecretB), "d-twin-b")).Status.Should().Be(HttpStatusCode.OK);

        (await PostAsync(body, Sign(body, SecretB), "d-twin-b")).Status.Should().Be(HttpStatusCode.Conflict,
            "el replay de cada webhook sigue rechazado");
    }
}
