import Link from "next/link";
import { redirect } from "next/navigation";
import { ExternalLink, Plus, Server, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageHeader } from "@/components/layout/page-header";
import { KpiCard } from "@/components/aethra/kpi-card";
import { MultiSelectFilter } from "@/components/aethra/MultiSelectFilter";
import { SavedViewsMenu } from "@/components/aethra/SavedViewsMenu";
import { getTranslations } from "next-intl/server";
import { serverFetch } from "@/lib/server-fetch";
import type { MachineOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface MachineFilters {
  q?: string;
  readiness?: string;
  acceptsPreviews?: string;
}

export default async function VmsPage({
  searchParams,
}: {
  searchParams: Promise<MachineFilters>;
}) {
  const t = await getTranslations("pages.vms");
  const c = await getTranslations("common");
  const filters = await searchParams;
  const query = buildQuery(filters);
  const data = await serverFetch<MachineOverviewDto[]>(`/api/ops/machines${query}`);
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error" || data === "notfound";
  const machines = Array.isArray(data) ? data : [];
  const ready = machines.filter((m) => m.readinessStatus === "ready").length;
  const offline = machines.filter((m) => m.readinessStatus === "offline").length;
  const degraded = machines.filter((m) => m.readinessStatus === "degraded").length;
  const previews = machines.reduce((sum, m) => sum + m.previewAppEnvironmentCount, 0);
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <div className="flex flex-wrap gap-2">
            <SavedViewsMenu storageKey="aethra.savedViews.machines" />
            <Button asChild>
              <Link href="/vms/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("action_register")}
              </Link>
            </Button>
          </div>
        }
      />

      <Card>
        <CardContent className="p-4">
          <form method="get" className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="q">{c("search")}</Label>
              <Input
                id="q"
                name="q"
                defaultValue={filters.q ?? ""}
                placeholder={t("filter_search_placeholder")}
                className="w-72"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="readiness">{t("filter_readiness")}</Label>
              <MultiSelectFilter
                id="readiness"
                name="readiness"
                value={filters.readiness}
                allLabel={c("all_f")}
                options={[
                  { value: "ready", label: "Ready" },
                  { value: "busy", label: "Busy" },
                  { value: "degraded", label: "Degraded" },
                  { value: "offline", label: "Offline" },
                  { value: "unknown", label: "Unknown" },
                ]}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="acceptsPreviews">{t("filter_preview_pool")}</Label>
              <select
                id="acceptsPreviews"
                name="acceptsPreviews"
                defaultValue={filters.acceptsPreviews ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">{c("all_f")}</option>
                <option value="true">{t("option_accepts_previews")}</option>
                <option value="false">{t("option_no_previews")}</option>
              </select>
            </div>
            <Button type="submit">{c("filter")}</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/vms">
                  <X className="mr-2 h-4 w-4" />
                  {c("clear")}
                </Link>
              </Button>
            ) : null}
          </form>
        </CardContent>
      </Card>

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      ) : machines.length === 0 ? (
        <EmptyState
          icon={<Server className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/vms/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("action_register")}
              </Link>
            </Button>
          }
        />
      ) : (
        <>
          <section className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <KpiCard
              label="Ready"
              value={ready}
              tone={ready > 0 ? "success" : "default"}
              icon={<Server className="h-4 w-4" />}
            />
            <KpiCard
              label="Degraded"
              value={degraded}
              tone={degraded > 0 ? "warning" : "success"}
              icon={<Server className="h-4 w-4" />}
            />
            <KpiCard
              label="Offline"
              value={offline}
              tone={offline > 0 ? "destructive" : "success"}
              icon={<Server className="h-4 w-4" />}
            />
            <KpiCard
              label="Preview app envs"
              value={previews}
              tone={previews > 0 ? "info" : "default"}
              icon={<Server className="h-4 w-4" />}
            />
          </section>

          <ul className="grid grid-cols-1 gap-4 xl:grid-cols-2">
            {machines.map((machine) => (
              <li key={machine.id}>
                <Card className="h-full">
                  <CardContent className="space-y-4 p-5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <Link
                          href={`/vms/${machine.id}`}
                          className="truncate text-base font-semibold text-foreground hover:text-primary"
                        >
                          {machine.name}
                        </Link>
                        <p className="mt-1 font-mono text-xs text-muted-foreground">
                          {machine.slug}
                        </p>
                      </div>
                      <div className="shrink-0 text-right">
                        <ReadinessBadge status={machine.readinessStatus} />
                        <p className="mt-1 font-mono text-[10px] uppercase text-muted-foreground">
                          {machine.status}
                        </p>
                      </div>
                    </div>
                    <p className="rounded-md border border-border bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
                      {machine.readinessReason}
                    </p>

                    <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
                      <Stat label="App envs" value={machine.appEnvironmentCount.toString()} />
                      <Stat label="Issues" value={machine.failingAppEnvironmentCount.toString()} />
                      <Stat label="Deploying" value={machine.deployingAppEnvironmentCount.toString()} />
                      <Stat label="Previews" value={machine.previewAppEnvironmentCount.toString()} />
                    </div>

                    <div className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-5">
                      <Stat label="Runtime" value={formatRuntime(machine)} />
                      <Stat label="Socket" value={formatRuntimeSocket(machine)} />
                      <Stat label="Memory" value={formatBytes(machine.totalMemoryBytes)} />
                      <Stat
                        label="Root disk free"
                        value={formatDiskFree(machine.rootDiskAvailableBytes, machine.rootDiskTotalBytes)}
                      />
                      <Stat
                        label="Data volume free"
                        value={formatDiskFree(machine.dataVolumeAvailableBytes, machine.dataVolumeTotalBytes)}
                      />
                    </div>

                    <div className="flex flex-wrap gap-2">
                      <Badge variant={machine.acceptsPreviews ? "info" : "outline"}>
                        {machine.acceptsPreviews ? "Preview pool" : "No previews"}
                      </Badge>
                      {machine.dataVolumePath ? (
                        <Badge variant="outline" className="font-mono">
                          data {machine.dataVolumePath}
                        </Badge>
                      ) : null}
                      <Badge variant="outline">Last seen {formatDate(machine.lastSeenAt ?? machine.updatedAt)}</Badge>
                    </div>

                    {machine.workloads.length > 0 ? (
                      <div className="space-y-2">
                        <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                          Workloads
                        </p>
                        <div className="space-y-2">
                          {machine.workloads.slice(0, 5).map((workload) => (
                            <div
                              key={workload.appEnvironmentId}
                              className="flex items-center justify-between gap-3 rounded-md border p-3"
                            >
                              <div className="min-w-0">
                                <Link
                                  href={`/instances/${workload.appEnvironmentId}`}
                                  className="block truncate text-sm font-medium hover:text-primary"
                                >
                                  {workload.appName}
                                </Link>
                                <p className="truncate text-xs text-muted-foreground">
                                  {workload.tenantName} / {workload.environment}
                                </p>
                              </div>
                              <div className="flex shrink-0 items-center gap-2">
                                {workload.publicUrl ? (
                                  <Link href={workload.publicUrl} target="_blank" className="text-primary">
                                    <ExternalLink className="h-3.5 w-3.5" />
                                  </Link>
                                ) : null}
                                <ReadinessBadge status={workload.healthStatus} />
                              </div>
                            </div>
                          ))}
                        </div>
                        {machine.workloads.length > 5 ? (
                          <p className="text-xs text-muted-foreground">
                            +{machine.workloads.length - 5} app environments mas
                          </p>
                        ) : null}
                      </div>
                    ) : (
                      <p className="rounded-md border border-dashed p-3 text-sm text-muted-foreground">
                        {t("no_app_envs")}
                      </p>
                    )}
                  </CardContent>
                </Card>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border bg-muted/20 p-3">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 text-lg font-semibold tabular-nums text-foreground">
        {value}
      </p>
    </div>
  );
}

function ReadinessBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant =
    normalized === "ready" || normalized === "healthy"
      ? "success"
      : normalized === "offline" || normalized === "failed"
        ? "destructive"
        : normalized === "busy" || normalized === "deploying"
          ? "info"
          : normalized === "degraded"
            ? "warning"
            : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function formatDate(value: string | null) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("es-CO", {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatBytes(value: number | null) {
  if (!value || value <= 0) return "-";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let unit = 0;
  let size = value;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  return `${size.toFixed(size >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function formatRuntime(machine: MachineOverviewDto) {
  if (!machine.containerRuntime) return "unknown";
  if (!machine.containerRuntimeVersion) return machine.containerRuntime;
  return machine.containerRuntimeVersion;
}

function formatRuntimeSocket(machine: MachineOverviewDto) {
  if (machine.runtimeSocketAccessible === null) return "unknown";
  return machine.runtimeSocketAccessible ? "accessible" : "blocked";
}

function formatDiskFree(available: number | null, total: number | null) {
  if (!available || !total || total <= 0) return "-";
  const percent = Math.round((available / total) * 100);
  return `${formatBytes(available)} (${percent}%)`;
}

function buildQuery(filters: MachineFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
