import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Activity, Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { KpiCard } from "@/components/aethra/kpi-card";
import { MonitorStatusPill } from "./MonitorStatusPill";
import { MonitorsLive } from "./MonitorsLive";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  MonitorOverviewDto,
  MonitorStatus,
  MonitorSummaryDto,
} from "@/lib/types";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

interface ListPageProps {
  searchParams: Promise<{
    status?: string;
    application_id?: string;
    enabled?: string;
  }>;
}

async function fetchMonitors(filters: {
  status?: string;
  applicationId?: string;
  enabled?: string;
}): Promise<MonitorSummaryDto[] | "unauthorized" | "error"> {
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
  const errored = data === "error";
  const list = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Monitores"
        description="Probes HTTP de uptime con check periódico y SignalR live."
        actions={
          <>
            <MonitorsLive />
            <Button asChild>
              <Link href="/monitors/new">
                <Plus className="mr-2 h-4 w-4" />
                Nuevo monitor
              </Link>
            </Button>
          </>
        }
      />

      {overview ? (
        <section className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
          <Link href={params.status === "Up" ? "/monitors" : "/monitors?status=Up"}>
            <KpiCard label="Up" value={overview.up} tone="success" />
          </Link>
          <Link href={params.status === "Degraded" ? "/monitors" : "/monitors?status=Degraded"}>
            <KpiCard label="Degraded" value={overview.degraded} tone="warning" />
          </Link>
          <Link href={params.status === "Down" ? "/monitors" : "/monitors?status=Down"}>
            <KpiCard label="Down" value={overview.down} tone="destructive" />
          </Link>
          <Link href={params.status === "Unknown" ? "/monitors" : "/monitors?status=Unknown"}>
            <KpiCard label="Unknown" value={overview.unknown} />
          </Link>
        </section>
      ) : null}

      <FiltersBar
        selectedStatus={params.status}
        selectedEnabled={params.enabled}
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado.
          </CardContent>
        </Card>
      ) : list.length === 0 ? (
        <EmptyState
          icon={Activity}
          title="Aún sin monitores"
          description="Creá tu primer monitor uptime para empezar a observar una URL."
          action={
            <Button asChild>
              <Link href="/monitors/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear monitor
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>URL</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Método</TableHead>
                <TableHead>Intervalo</TableHead>
                <TableHead>Último check</TableHead>
                <TableHead>Fallos seguidos</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.map((m) => (
                <TableRow key={m.id}>
                  <TableCell>
                    <Link
                      href={`/monitors/${m.id}`}
                      className="font-medium text-foreground hover:text-primary"
                    >
                      {m.name}
                    </Link>
                    <div className="font-mono text-[11px] text-muted-foreground">
                      {m.slug}
                    </div>
                  </TableCell>
                  <TableCell
                    className="max-w-xs truncate font-mono text-xs text-foreground"
                    title={m.url}
                  >
                    {m.url}
                  </TableCell>
                  <TableCell>
                    <MonitorStatusPill
                      status={m.status}
                      disabled={!m.is_enabled}
                    />
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {m.http_method}
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {m.interval_sec}s
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatRelative(m.last_checked_at)}
                  </TableCell>
                  <TableCell>
                    <FailuresBadge n={m.consecutive_failures} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
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
    <div className="mb-4 flex flex-wrap items-center gap-2 text-xs">
      <span className="text-muted-foreground">Filtros:</span>
      <FilterChip
        label="Todos"
        href="/monitors"
        active={!selectedStatus && !selectedEnabled}
      />
      {(["Up", "Down", "Degraded", "Unknown"] as MonitorStatus[]).map((s) => (
        <FilterChip
          key={s}
          label={s}
          href={`/monitors?status=${s}`}
          active={selectedStatus === s}
        />
      ))}
      <span className="mx-2 text-muted-foreground/50">|</span>
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
      className={cn(
        "rounded-full border px-3 py-1 transition",
        active
          ? "border-primary/40 bg-primary/10 text-primary"
          : "border-border text-muted-foreground hover:border-foreground/40",
      )}
    >
      {label}
    </Link>
  );
}

function FailuresBadge({ n }: { n: number }) {
  if (n === 0) {
    return <span className="text-xs text-muted-foreground">0</span>;
  }
  return (
    <Badge variant={n >= 3 ? "destructive" : "warning"}>{n}</Badge>
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
