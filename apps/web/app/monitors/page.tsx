import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { MonitorOverviewDto, MonitorStatus, MonitorSummaryDto } from "@/lib/types";
import { MonitorStatusPill } from "./MonitorStatusPill";
import { MonitorsLive } from "./MonitorsLive";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

interface ListPageProps {
  searchParams: Promise<{ status?: string; application_id?: string; enabled?: string }>;
}

async function fetchMonitors(
  filters: { status?: string; applicationId?: string; enabled?: string },
): Promise<MonitorSummaryDto[] | "unauthorized" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const qs = new URLSearchParams();
  if (filters.status) qs.set("status", filters.status);
  if (filters.applicationId) qs.set("application_id", filters.applicationId);
  if (filters.enabled !== undefined) qs.set("enabled", filters.enabled);
  const query = qs.toString();
  const url = `${API_URL}/api/monitors/${query ? `?${query}` : ""}`;
  const res = await fetch(url, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as MonitorSummaryDto[];
}

async function fetchOverview(): Promise<MonitorOverviewDto | null> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/monitors/overview`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return (await res.json()) as MonitorOverviewDto;
}

export default async function MonitorsPage({ searchParams }: ListPageProps) {
  const params = await searchParams;
  const data = await fetchMonitors({
    status: params.status,
    applicationId: params.application_id,
    enabled: params.enabled,
  });
  if (data === "unauthorized") {
    redirect("/login");
  }
  const overview = await fetchOverview();

  const list = Array.isArray(data) ? data : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Monitores</h1>
            <p className="text-sm text-zinc-500">
              Probes HTTP de uptime con check periódico y SignalR live.
            </p>
          </div>
          <div className="flex items-center gap-3">
            <MonitorsLive />
            <Link
              href="/monitors/new"
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              Nuevo monitor
            </Link>
          </div>
        </header>

        {overview && <OverviewCards overview={overview} active={params.status} />}

        <FiltersBar selectedStatus={params.status} selectedEnabled={params.enabled} />

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado.
          </div>
        )}

        {list.length === 0 ? (
          <EmptyState />
        ) : (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Nombre</th>
                  <th className="px-4 py-3">URL</th>
                  <th className="px-4 py-3">Estado</th>
                  <th className="px-4 py-3">Método</th>
                  <th className="px-4 py-3">Intervalo</th>
                  <th className="px-4 py-3">Último check</th>
                  <th className="px-4 py-3">Fallos seguidos</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {list.map((m) => (
                  <tr key={m.id} className="hover:bg-zinc-900/60">
                    <td className="px-4 py-3">
                      <Link
                        href={`/monitors/${m.id}`}
                        className="font-medium text-zinc-100 hover:text-emerald-300"
                      >
                        {m.name}
                      </Link>
                      <div className="font-mono text-[11px] text-zinc-500">{m.slug}</div>
                    </td>
                    <td className="max-w-xs truncate px-4 py-3 font-mono text-xs text-zinc-300" title={m.url}>
                      {m.url}
                    </td>
                    <td className="px-4 py-3">
                      <MonitorStatusPill
                        status={m.status}
                        disabled={!m.is_enabled}
                      />
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-300">
                      {m.http_method}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-400">
                      {m.interval_sec}s
                    </td>
                    <td className="px-4 py-3 text-xs text-zinc-400">
                      {formatRelative(m.last_checked_at)}
                    </td>
                    <td className="px-4 py-3">
                      <FailuresBadge n={m.consecutive_failures} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </main>
  );
}

function OverviewCards({
  overview,
  active,
}: {
  overview: MonitorOverviewDto;
  active: string | undefined;
}) {
  const cards: { key: string; label: string; value: number; accent: string }[] = [
    { key: "Up", label: "Up", value: overview.up, accent: "emerald" },
    { key: "Degraded", label: "Degraded", value: overview.degraded, accent: "amber" },
    { key: "Down", label: "Down", value: overview.down, accent: "rose" },
    { key: "Unknown", label: "Unknown", value: overview.unknown, accent: "zinc" },
  ];
  return (
    <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
      {cards.map((c) => {
        const isActive = (active ?? "") === c.key;
        const accentColor =
          c.accent === "emerald"
            ? "text-emerald-300"
            : c.accent === "amber"
              ? "text-amber-300"
              : c.accent === "rose"
                ? "text-rose-300"
                : "text-zinc-300";
        return (
          <Link
            key={c.key}
            href={isActive ? "/monitors" : `/monitors?status=${c.key}`}
            className={`rounded-2xl border bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 ${
              isActive ? "border-emerald-500/40" : "border-zinc-800"
            }`}
          >
            <div className="text-[10px] uppercase tracking-wider text-zinc-500">
              {c.label}
            </div>
            <div className={`mt-2 text-3xl font-semibold ${accentColor}`}>
              {c.value}
            </div>
            <div className="mt-1 text-[11px] text-zinc-500">
              de {overview.total} activos · {overview.disabled} desactivados
            </div>
          </Link>
        );
      })}
    </section>
  );
}

function FiltersBar({
  selectedStatus,
  selectedEnabled,
}: {
  selectedStatus: string | undefined;
  selectedEnabled: string | undefined;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2 text-xs">
      <span className="text-zinc-500">Filtros:</span>
      <FilterChip label="Todos" href="/monitors" active={!selectedStatus && !selectedEnabled} />
      {(["Up", "Down", "Degraded", "Unknown"] as MonitorStatus[]).map((s) => (
        <FilterChip
          key={s}
          label={s}
          href={`/monitors?status=${s}`}
          active={selectedStatus === s}
        />
      ))}
      <span className="mx-2 text-zinc-700">|</span>
      <FilterChip
        label="Habilitados"
        href="/monitors?enabled=true"
        active={selectedEnabled === "true"}
      />
      <FilterChip
        label="Deshabilitados"
        href="/monitors?enabled=false"
        active={selectedEnabled === "false"}
      />
    </div>
  );
}

function FilterChip({
  label,
  href,
  active,
}: {
  label: string;
  href: string;
  active: boolean;
}) {
  return (
    <Link
      href={href}
      className={`rounded-full border px-3 py-1 transition ${
        active
          ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-300"
          : "border-zinc-700 text-zinc-300 hover:border-zinc-500"
      }`}
    >
      {label}
    </Link>
  );
}

function FailuresBadge({ n }: { n: number }) {
  if (n === 0) {
    return <span className="text-xs text-zinc-500">0</span>;
  }
  const cls = n >= 3 ? "text-rose-300" : "text-amber-300";
  return <span className={`font-mono text-xs ${cls}`}>{n}</span>;
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aún sin monitores</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea tu primer monitor uptime para empezar a observar una URL.
      </p>
      <Link
        href="/monitors/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Crear monitor
      </Link>
    </div>
  );
}

function formatRelative(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const seconds = Math.floor((Date.now() - d.getTime()) / 1000);
  if (seconds < 0) return d.toLocaleString();
  if (seconds < 60) return `hace ${seconds}s`;
  if (seconds < 3600) return `hace ${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `hace ${Math.floor(seconds / 3600)}h`;
  return d.toLocaleString();
}
