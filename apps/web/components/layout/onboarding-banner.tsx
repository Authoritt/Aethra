import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { CircleAlert, ArrowRight } from "lucide-react";
import { serverFetch } from "@/lib/server-fetch";

/**
 * DTOs mínimos para evaluar si el onboarding está completo. Los endpoints
 * reales devuelven más campos — usamos el subset que necesitamos.
 */
interface DomainDto {
  id: string;
  domain: string;
  isActive?: boolean;
  active?: boolean;
}
interface EnvironmentDto {
  id: string;
  name?: string;
}
interface IntegrationDto {
  id: string;
  kind?: string;
}

interface Check {
  ok: boolean;
  message: string;
  ctaLabel: string;
  ctaHref: string;
}

/**
 * Banner condicional que avisa al usuario qué falta configurar para que
 * la plataforma sea totalmente funcional. Es un Server Component: pre-fetcha
 * las 3 colecciones en paralelo y solo renderea si hay algo pendiente.
 *
 * Si la API es inalcanzable (SSR sin dominio, backend caído, etc) devolvemos
 * `null` para no bloquear el dashboard. Preferimos silencio a falsos positivos.
 */
export async function OnboardingBanner() {
  const t = await getTranslations("onboarding");
  let domains: ReadonlyArray<DomainDto> = [];
  let environments: ReadonlyArray<EnvironmentDto> = [];
  let integrations: ReadonlyArray<IntegrationDto> = [];

  try {
    const [domainsRes, environmentsRes, integrationsRes] = await Promise.all([
      serverFetch<DomainDto[]>("/api/settings/domains"),
      serverFetch<EnvironmentDto[]>("/api/settings/environments"),
      serverFetch<IntegrationDto[]>("/api/settings/integrations"),
    ]);

    if (Array.isArray(domainsRes)) domains = domainsRes;
    if (Array.isArray(environmentsRes)) environments = environmentsRes;
    if (Array.isArray(integrationsRes)) integrations = integrationsRes;
  } catch {
    // Sin API alcanzable: ocultamos en lugar de mostrar un banner falso.
    return null;
  }

  const hasActiveDomain = domains.some((d) => d.isActive ?? d.active ?? false);

  const checks: Check[] = [];
  if (!hasActiveDomain) {
    checks.push({
      ok: false,
      message: t("no_active_domain"),
      ctaLabel: t("no_active_domain_cta"),
      ctaHref: "/settings/domains",
    });
  }
  if (environments.length === 0) {
    checks.push({
      ok: false,
      message: t("no_environment"),
      ctaLabel: t("no_environment_cta"),
      ctaHref: "/settings/environments",
    });
  }
  if (integrations.length === 0) {
    checks.push({
      ok: false,
      message: t("no_integration"),
      ctaLabel: t("no_integration_cta"),
      ctaHref: "/settings/integrations",
    });
  }

  if (checks.length === 0) return null;

  return (
    <div
      role="status"
      className="rounded-lg border border-emerald-500/30 bg-emerald-500/[0.06] p-4 text-foreground shadow-sm"
    >
      <div className="flex items-start gap-3">
        <CircleAlert
          className="mt-0.5 size-5 shrink-0 text-emerald-500"
          aria-hidden="true"
        />
        <div className="flex-1">
          <h3 className="text-sm font-semibold">{t("title")}</h3>
          <p className="mt-0.5 text-sm text-muted-foreground">{t("intro")}</p>
          <ul className="mt-3 space-y-2">
            {checks.map((check) => (
              <li
                key={check.ctaHref}
                className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-background/50 px-3 py-2 text-sm"
              >
                <span className="text-foreground/90">{check.message}</span>
                <Link
                  href={check.ctaHref}
                  className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600 transition-colors hover:text-emerald-500 dark:text-emerald-400 dark:hover:text-emerald-300"
                >
                  {check.ctaLabel}
                  <ArrowRight className="size-3.5" aria-hidden="true" />
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}
