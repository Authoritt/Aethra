## What this changes

<!-- One or two sentences. If it fixes an issue, "Fixes #123". -->

## Why

<!-- The failure or the need behind it. For a bug: what was actually happening, ideally with the
     file:line where it went wrong. This is the part reviewers read most carefully. -->

## How it was verified

<!-- Be specific and honest. "dotnet test passes" plus what you exercised by hand. -->

- [ ] `dotnet build Aethra.slnx` is clean
- [ ] `dotnet test Aethra.slnx` passes (architecture fences included)
- [ ] Tried it against a running instance

**What I did not verify:**

<!-- Anything you could not test — no Docker locally, no Cloudflare account, no second VM. An honest
     gap here is welcome and useful. A silent one is what breaks production. -->

## Checklist

- [ ] Stays inside one module, or crosses boundaries only through `IIntegrationEvent`
- [ ] Domain layer still free of EF Core / ASP.NET references
- [ ] Expected failures return `Result<T>` errors instead of throwing
- [ ] Time comes from `IClock`, not `DateTime.UtcNow`
- [ ] No secrets, tokens or real hostnames in the diff

<!-- Touching auth, scopes, secrets, webhook signatures or the proxy? Say so here — those get read
     closely, and that is about blast radius, not about you. -->
