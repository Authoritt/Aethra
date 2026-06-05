import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
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
  const t = await getTranslations("pages.monitors_list");
  const tCommon = await getTranslations("common");
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
        title={t("title")}
        description={t("description")}
        actions={
          <>
            <MonitorsLive />
            <Button asChild>
              <Link href="/monitors/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("new_monitor")}
              </Link>
            </Button>
          </>
        }
      />

      {overview ? (
        <section className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
          <Link href={params.status === "Up" ? "/monitors" : "/monitors?status=Up"}>
            <KpiCard label={t("kpi_up")} value={overview.up} tone="success" />
          </Link>
          <Link href={params.status === "Degraded" ? "/monitors" : "/monitors?status=Degraded"}>
            <KpiCard label={t("kpi_degraded")} value={overview.degraded} tone="warning" />
          </Link>
          <Link href={params.status === "Down" ? "/monitors" : "/monitors?status=Down"}>
            <KpiCard label={t("kpi_down")} value={overview.down} tone="destructive" />
          </Link>
          <Link href={params.status === "Unknown" ? "/monitors" : "/monitors?status=Unknown"}>
            <KpiCard label={t("kpi_unknown")} value={overview.unknown} />
          </Link>
        </section>
      ) : null}

      <FiltersBar
        selectedStatus={params.status}
        selectedEnabled={params.enabled}
        filtersLabel={t("filters_label")}
        allLabel={t("filter_all")}
        enabledLabel={t("filter_enabled")}
        disabledLabel={t("filter_disabled")}
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : list.length === 0 ? (
        <EmptyState
          icon={<Activity className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/monitors/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("create_monitor")}
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("col_name")}</TableHead>
                <TableHead>{t("col_url")}</TableHead>
                <TableHead>{t("col_status")}</TableHead>
                <TableHead>{t("col_method")}</TableHead>
                <TableHead>{t("col_interval")}</TableHead>
                <TableHead>{t("col_last_check")}</TableHead>
                <TableHead>{t("col_failures")}</TableHead>
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
                      disabled={!m.isEnabled}
                    />
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {m.httpMethod}
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {m.intervalSec}s
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatRelative(m.lastCheckedAt, t)}
                  </TableCell>
                  <TableCell>
                    <FailuresBadge n={m.consecutiveFailures} />
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
  filtersLabel,
  allLabel,
  enabledLabel,
  disabledLabel,
}: {
  selectedStatus: string | undefined;
  selectedEnabled: string | undefined;
  filtersLabel: string;
  allLabel: string;
  enabledLabel: string;
  disabledLabel: string;
}) {
  return (
    <div className="mb-4 flex flex-wrap items-center gap-2 text-xs">
      <span className="text-muted-foreground">{filtersLabel}</span>
      <FilterChip
        label={allLabel}
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
        label={enabledLabel}
        href="/monitors?enabled=true"
        active={selectedEnabled === "true"}
      />
      <FilterChip
        label={disabledLabel}
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

type Translator = (
  key: string,
  values?: Record<string, string | number | Date>,
) => string;

function formatRelative(iso: string | null, t: Translator): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const seconds = Math.floor((Date.now() - d.getTime()) / 1000);
  if (seconds < 0) return d.toLocaleString();
  if (seconds < 60) return t("relative_seconds_ago", { seconds });
  if (seconds < 3600) return t("relative_minutes_ago", { minutes: Math.floor(seconds / 60) });
  if (seconds < 86400) return t("relative_hours_ago", { hours: Math.floor(seconds / 3600) });
  return d.toLocaleString();
}
