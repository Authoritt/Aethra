import Link from "next/link";
import { redirect } from "next/navigation";
import { cookies } from "next/headers";
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

async function getMe(): Promise<MeResponse | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

async function getContext(): Promise<ContextResponse | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/context`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

async function getMonitorOverview(): Promise<MonitorOverviewDto | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/monitors/overview`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

export default async function Dashboard() {
  const me = await getMe();
  if (!me) {
    redirect("/login");
  }
  const ctx = await getContext();
  const monitorOverview = await getMonitorOverview();

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Dashboard</h1>
            <p className="text-sm text-zinc-500">{me.email}</p>
          </div>
          <Link
            href="/"
            className="rounded-full border border-zinc-700 px-4 py-2 text-sm transition hover:bg-zinc-800"
          >
            Volver
          </Link>
        </header>

        <section className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <Stat label="Proyectos" value={ctx?.projects.length ?? 0} />
          <Stat label="VMs" value={ctx?.vms.length ?? 0} />
          <Stat label="Servicios" value={ctx?.services.length ?? 0} />
          <Stat label="Zonas CF" value={ctx?.cloudflare_zones.length ?? 0} />
        </section>

        <section className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
          <NavCard
            href="/projects"
            title="Proyectos"
            description="Agrupaciones lógicas que contendrán templates y clients."
          />
          <NavCard
            href="/templates"
            title="Templates"
            description="Plantillas reutilizables de servicios multi-tenant."
            badge={0}
          />
          <NavCard
            href="/clients"
            title="Clients"
            description="Cuentas tenant que instancian un template."
            badge={0}
          />
          <NavCard
            href="/vms"
            title="VMs"
            description="Hosts Oracle gestionados con satélite y métricas."
          />
          <NavCard
            href="/routes"
            title="Rutas"
            description="Reverse proxy YARP con TLS Let's Encrypt."
          />
          <NavCard
            href="/services"
            title="Servicios compartidos"
            description="Postgres, Redis y otros backends bindeables."
            badge={ctx?.services.length ?? 0}
          />
          <NavCard
            href="/cloudflare"
            title="Cloudflare"
            description="Zonas DNS y records gestionados via API v4."
            badge={ctx?.cloudflare_zones.length ?? 0}
          />
          <NavCard
            href="/settings/api-keys"
            title="API keys"
            description="Tokens portadores para integrar agentes IA y herramientas externas."
          />
          <Link
            href="/monitors"
            className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
          >
            <div className="flex items-start justify-between gap-2">
              <h3 className="text-lg font-semibold text-zinc-100">Monitores uptime</h3>
              {monitorOverview && (
                <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[11px] text-zinc-300">
                  {monitorOverview.total}
                </span>
              )}
            </div>
            <p className="mt-1 text-sm text-zinc-400">
              Probes HTTP periódicos contra tus apps con SignalR live.
            </p>
            {monitorOverview && (
              <div className="mt-3 flex gap-2 text-[11px] font-medium">
                <span className="rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-emerald-300">
                  Up {monitorOverview.up}
                </span>
                {monitorOverview.degraded > 0 && (
                  <span className="rounded-full border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 text-amber-300">
                    Deg {monitorOverview.degraded}
                  </span>
                )}
                {monitorOverview.down > 0 && (
                  <span className="rounded-full border border-rose-500/40 bg-rose-500/10 px-2 py-0.5 text-rose-300">
                    Down {monitorOverview.down}
                  </span>
                )}
              </div>
            )}
          </Link>
        </section>

        <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6">
          <h2 className="mb-4 text-sm uppercase tracking-wider text-zinc-500">
            Refactor F9 — multi-tenant
          </h2>
          <ol className="space-y-2 text-sm text-zinc-300">
            <li>F9.0 — cleanup frontend (en curso)</li>
            <li>F9.3 — Pipeline de Build y Deployments</li>
            <li>F9.5 — Templates, Clients e Instances</li>
          </ol>
        </section>
      </div>
    </main>
  );
}

function NavCard({
  href,
  title,
  description,
  badge,
}: {
  href: string;
  title: string;
  description: string;
  badge?: number;
}) {
  return (
    <Link
      href={href}
      className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="text-lg font-semibold text-zinc-100">{title}</h3>
        {typeof badge === "number" && (
          <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[11px] text-zinc-300">
            {badge}
          </span>
        )}
      </div>
      <p className="mt-1 text-sm text-zinc-400">{description}</p>
    </Link>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-zinc-800 bg-zinc-900/40 p-4">
      <div className="text-xs uppercase tracking-wider text-zinc-500">
        {label}
      </div>
      <div className="text-3xl font-semibold text-zinc-100">{value}</div>
    </div>
  );
}
