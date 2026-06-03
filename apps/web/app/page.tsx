import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ArrowRight, CheckCircle2, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Logo } from "@/components/brand/logo";
import { StatusPill } from "@/components/ui/status-pill";
import { API_URL } from "@/lib/api";

// La página depende del estado de la API en tiempo de request, no en build time.
// Sin esto, `next build` intenta prerenderizar y se cuelga esperando al servidor.
export const dynamic = "force-dynamic";

interface HealthResponse {
  status: string;
  service: string;
  time: string;
  version: string;
}

async function fetchHealth(): Promise<HealthResponse | { error: string }> {
  try {
    const res = await fetch(`${API_URL}/health`, { cache: "no-store" });
    if (!res.ok) return { error: `${res.status}` };
    return await res.json();
  } catch (e) {
    return { error: e instanceof Error ? e.message : "unreachable" };
  }
}

export default async function Home() {
  const t = await getTranslations("pages.landing");
  const health = await fetchHealth();
  const ok = "status" in health && health.status === "ok";

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-10 bg-background px-6 py-20 text-foreground">
      <div className="flex flex-col items-center gap-3">
        <Logo variant="lockup" />
        <p className="max-w-md text-center text-muted-foreground">
          {t("tagline")}
        </p>
      </div>

      <Card className="w-full max-w-md">
        <CardContent className="flex flex-col gap-4 p-6">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              {t("api_label")}
            </span>
            {ok ? (
              <StatusPill variant="success">
                <CheckCircle2 className="h-3 w-3" />
                {t("operational")}
              </StatusPill>
            ) : (
              <StatusPill variant="destructive">
                <XCircle className="h-3 w-3" />
                {t("unreachable")}
              </StatusPill>
            )}
          </div>
          <pre className="overflow-x-auto rounded-md border border-border bg-muted/40 p-3 font-mono text-xs text-foreground">
            {JSON.stringify(health, null, 2)}
          </pre>
          <p className="text-xs text-muted-foreground">
            {t("url_label")} <code className="font-mono text-foreground">{API_URL}</code>
          </p>
        </CardContent>
      </Card>

      <div className="flex flex-wrap items-center justify-center gap-3">
        <Button asChild>
          <Link href="/login">
            {t("login_cta")}
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
        <Button asChild variant="outline">
          <a
            href={`${API_URL}/openapi/v1.json`}
            target="_blank"
            rel="noopener noreferrer"
          >
            {t("openapi_cta")}
          </a>
        </Button>
      </div>

      <footer className="mt-8 text-xs text-muted-foreground">
        {t("footer")}
      </footer>
    </main>
  );
}
