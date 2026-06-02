import Link from "next/link";
import { redirect } from "next/navigation";
import { cookies } from "next/headers";
import {
  Activity,
  Boxes,
  Cloud,
  FolderKanban,
  Network,
  Rocket,
  Server,
  Settings,
} from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { PageHeader } from "@/components/layout/page-header";
import { KpiCard } from "@/components/aethra/kpi-card";
import { MonitorStatusPill } from "@/components/aethra/monitor-status-pill";
import { API_URL } from "@/lib/api";
import type { MonitorOverviewDto } from "@/lib/types";

// Lee cookies en request — siempre dinámico.
export const dynamic = "force-dynamic";

interface MeResponse {
  email: string;
  scopes: string[];
}

interface ContextResponse {
  projects: unknown[];
  vms: unknown[];
  services: unknown[];
  cloudflare_zones: unknown[];
  generated_at: string;
}

async function fetchJson<T>(path: string): Promise<T | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}${path}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

export default async function Dashboard() {
  const me = await fetchJson<MeResponse>("/auth/me");
  if (!me) {
    redirect("/login");
  }
  const ctx = await fetchJson<ContextResponse>("/context");
  const monitorOverview = await fetchJson<MonitorOverviewDto>(
    "/api/monitors/overview",
  );

  const navCards: Array<{
    href: string;
    title: string;
    description: string;
    icon: typeof FolderKanban;
    badge?: number;
  }> = [
    {
      href: "/projects",
      title: "Proyectos",
      description: "Agrupaciones lógicas con templates y clients multi-tenant.",
      icon: FolderKanban,
      badge: ctx?.projects.length ?? 0,
    },
    {
      href: "/vms",
      title: "VMs",
      description: "Hosts gestionados con satélite y métricas en vivo.",
      icon: Server,
      badge: ctx?.vms.length ?? 0,
    },
    {
      href: "/services",
      title: "Servicios compartidos",
      description: "Postgres, Redis y otros backends bindeables.",
      icon: Boxes,
      badge: ctx?.services.length ?? 0,
    },
    {
      href: "/cloudflare",
      title: "Cloudflare DNS",
      description: "Zonas y records gestionados via API v4.",
      icon: Cloud,
      badge: ctx?.cloudflare_zones.length ?? 0,
    },
    {
      href: "/routes",
      title: "Rutas",
      description: "Reverse proxy YARP con TLS Let's Encrypt.",
      icon: Network,
    },
    {
      href: "/builds",
      title: "Builds",
      description: "Git → imagen Docker, pipeline en vivo.",
      icon: Rocket,
    },
    {
      href: "/deployments",
      title: "Deployments",
      description: "Despliegues blue-green hacia instancias.",
      icon: Activity,
    },
    {
      href: "/settings",
      title: "Settings",
      description: "Integraciones, dominios, environments y API keys.",
      icon: Settings,
    },
  ];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Dashboard"
        description={
          <>
            Bienvenida, <span className="text-foreground">{me.email}</span>.
            Esto es lo que está pasando ahora mismo.
          </>
        }
      />

      <section className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <KpiCard
          label="Proyectos"
          value={ctx?.projects.length ?? 0}
          icon={<FolderKanban className="h-4 w-4" />}
        />
        <KpiCard
          label="VMs"
          value={ctx?.vms.length ?? 0}
          icon={<Server className="h-4 w-4" />}
        />
        <KpiCard
          label="Servicios"
          value={ctx?.services.length ?? 0}
          icon={<Boxes className="h-4 w-4" />}
        />
        <KpiCard
          label="Monitores down"
          value={monitorOverview?.down ?? 0}
          icon={<Activity className="h-4 w-4" />}
          tone={(monitorOverview?.down ?? 0) > 0 ? "destructive" : "success"}
        />
      </section>

      {monitorOverview ? (
        <Card className="mt-6">
          <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
            <div className="space-y-1">
              <CardTitle className="text-base">Monitores uptime</CardTitle>
              <CardDescription>
                Estado actual de los probes HTTP gestionados por el módulo
                Monitoring.
              </CardDescription>
            </div>
            <Link
              href="/monitors"
              className="text-xs font-medium text-primary hover:underline underline-offset-4"
            >
              Ver todos →
            </Link>
          </CardHeader>
          <CardContent className="flex flex-wrap items-center gap-2">
            <Badge variant="outline">Total {monitorOverview.total}</Badge>
            <MonitorStatusPill status="up" />
            <span className="text-sm tabular-nums text-muted-foreground">
              {monitorOverview.up}
            </span>
            {monitorOverview.degraded > 0 ? (
              <>
                <MonitorStatusPill status="degraded" />
                <span className="text-sm tabular-nums text-muted-foreground">
                  {monitorOverview.degraded}
                </span>
              </>
            ) : null}
            {monitorOverview.down > 0 ? (
              <>
                <MonitorStatusPill status="down" />
                <span className="text-sm tabular-nums text-muted-foreground">
                  {monitorOverview.down}
                </span>
              </>
            ) : null}
            {monitorOverview.disabled > 0 ? (
              <>
                <MonitorStatusPill status="disabled" enabled={false} />
                <span className="text-sm tabular-nums text-muted-foreground">
                  {monitorOverview.disabled}
                </span>
              </>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <section className="mt-8 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {navCards.map((c) => (
          <Link key={c.href} href={c.href} className="group">
            <Card className="h-full transition-colors group-hover:border-primary/40">
              <CardContent className="flex flex-col gap-2 p-5">
                <div className="flex items-start justify-between">
                  <div className="flex h-9 w-9 items-center justify-center rounded-md bg-muted text-muted-foreground transition-colors group-hover:bg-primary/10 group-hover:text-primary">
                    <c.icon className="h-4 w-4" />
                  </div>
                  {typeof c.badge === "number" ? (
                    <Badge variant="outline">{c.badge}</Badge>
                  ) : null}
                </div>
                <h3 className="mt-1 text-base font-semibold text-foreground">
                  {c.title}
                </h3>
                <p className="text-sm text-muted-foreground">{c.description}</p>
              </CardContent>
            </Card>
          </Link>
        ))}
      </section>
    </div>
  );
}
