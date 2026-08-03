import Link from "next/link";
import { redirect } from "next/navigation";
import {
  AlertTriangle,
  ExternalLink,
  GitBranch,
  MonitorCheck,
  Network,
  Rocket,
  Server,
  SquareStack,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { KpiCard } from "@/components/aethra/kpi-card";
import { DiskUsageCard } from "@/components/aethra/disk-usage-card";
import { NodeDiskCard } from "@/components/aethra/node-disk-card";
import { getLocale, getTranslations } from "next-intl/server";
import { serverFetch } from "@/lib/server-fetch";
import type {
  AppEnvironmentOverviewDto,
  AppOverviewDto,
  DatabaseDiskUsageDto,
  MachineOverviewDto,
  OperationalIssueDto,
  PublicEndpointOverviewDto,
  ReleaseOverviewDto,
} from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function Dashboard() {
  const t = await getTranslations("pages.dashboard");
  const locale = await getLocale();
  const [appsData, envsData, releasesData, endpointsData, issuesData, machinesData, diskData] =
    await Promise.all([
      serverFetch<AppOverviewDto[]>("/api/ops/apps"),
      serverFetch<AppEnvironmentOverviewDto[]>("/api/ops/app-environments"),
      serverFetch<ReleaseOverviewDto[]>("/api/ops/releases"),
      serverFetch<PublicEndpointOverviewDto[]>("/api/ops/public-endpoints"),
      serverFetch<OperationalIssueDto[]>("/api/ops/operational-issues"),
      serverFetch<MachineOverviewDto[]>("/api/ops/machines"),
      serverFetch<DatabaseDiskUsageDto>("/api/metrics/database?top=12"),
    ]);

  if (
    appsData === "unauthorized" ||
    envsData === "unauthorized" ||
    releasesData === "unauthorized" ||
    endpointsData === "unauthorized" ||
    issuesData === "unauthorized" ||
    machinesData === "unauthorized"
  ) {
    redirect("/login");
  }

  const loadError = [appsData, envsData, releasesData, endpointsData, issuesData, machinesData].some(
    (x) => x === "error" || x === "notfound",
  );
  const apps = Array.isArray(appsData) ? appsData : [];
  const envs = Array.isArray(envsData) ? envsData : [];
  const releases = Array.isArray(releasesData) ? releasesData : [];
  const endpoints = Array.isArray(endpointsData) ? endpointsData : [];
  const issues = Array.isArray(issuesData) ? issuesData : [];
  const machines = Array.isArray(machinesData) ? machinesData : [];

  const activeReleases = releases.filter((r) => r.status === "active").length;
  const brokenEndpoints = endpoints.filter((e) => e.healthStatus === "broken").length;
  const criticalIssues = issues.filter((i) => i.severity === "critical").length;
  const machinesNotReady = machines.filter((m) => m.readinessStatus !== "ready").length;
  const unhealthyEnvs = envs.filter((e) => e.healthStatus !== "healthy").length;
  const topIssues = issues.slice(0, 6);
  const recentReleases = releases.slice(0, 5);
  const brokenEndpointRows = endpoints.filter((e) => e.healthStatus !== "healthy").slice(0, 5);
  const machineRows = machines.slice(0, 6);
  const disk =
    diskData &&
    diskData !== "unauthorized" &&
    diskData !== "notfound" &&
    diskData !== "error"
      ? diskData
      : null;

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="outline">
              <Link href="/releases">
                <Rocket className="mr-2 h-4 w-4" />
                {t("action_releases")}
              </Link>
            </Button>
            <Button asChild>
              <Link href="/app-environments">
                <SquareStack className="mr-2 h-4 w-4" />
                {t("action_app_environments")}
              </Link>
            </Button>
          </div>
        }
      />

      {loadError ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      ) : null}

      <section className="grid grid-cols-2 gap-4 lg:grid-cols-6">
        <KpiCard
          label={t("kpi_apps")}
          value={apps.length}
          delta={t("kpi_apps_delta", { count: envs.length })}
          icon={<SquareStack className="h-4 w-4" />}
        />
        <KpiCard
          label={t("kpi_envs_alert")}
          value={unhealthyEnvs}
          tone={unhealthyEnvs > 0 ? "warning" : "success"}
          icon={<MonitorCheck className="h-4 w-4" />}
        />
        <KpiCard
          label={t("kpi_releases_active")}
          value={activeReleases}
          tone={activeReleases > 0 ? "info" : "default"}
          icon={<Rocket className="h-4 w-4" />}
        />
        <KpiCard
          label={t("kpi_endpoints_broken")}
          value={brokenEndpoints}
          tone={brokenEndpoints > 0 ? "destructive" : "success"}
          icon={<Network className="h-4 w-4" />}
        />
        <KpiCard
          label={t("kpi_issues_critical")}
          value={criticalIssues}
          tone={criticalIssues > 0 ? "destructive" : "success"}
          icon={<AlertTriangle className="h-4 w-4" />}
        />
        <KpiCard
          label={t("kpi_machines_not_ready")}
          value={machinesNotReady}
          tone={machinesNotReady > 0 ? "warning" : "success"}
          icon={<Server className="h-4 w-4" />}
        />
      </section>

      <section className="grid grid-cols-1 gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card>
          <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
            <CardTitle className="text-base">{t("issues_title")}</CardTitle>
            <Link href="/operational-issues" className="text-xs font-medium text-primary hover:underline">
              {t("issues_link")}
            </Link>
          </CardHeader>
          <CardContent className="space-y-3">
            {topIssues.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("issues_empty")}</p>
            ) : (
              topIssues.map((issue) => (
                <Link
                  key={issue.id}
                  href={issue.suggestedHref ?? "/operational-issues"}
                  className="block rounded-md border p-3 transition-colors hover:border-primary/40"
                >
                  <div className="flex items-start justify-between gap-4">
                    <div className="min-w-0 space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <SeverityBadge severity={issue.severity} />
                        <span className="truncate text-sm font-medium">{issue.title}</span>
                      </div>
                      <p className="truncate text-xs text-muted-foreground">
                        {[issue.appName, issue.tenantName, issue.environment].filter(Boolean).join(" / ") || issue.resourceType}
                      </p>
                    </div>
                    <span className="shrink-0 font-mono text-[10px] uppercase text-muted-foreground">
                      {issue.code}
                    </span>
                  </div>
                  <div className="mt-3 flex justify-end">
                    <Badge variant="outline" className="text-[10px]">
                      {issue.suggestedAction}
                    </Badge>
                  </div>
                </Link>
              ))
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
            <CardTitle className="text-base">{t("releases_title")}</CardTitle>
            <Link href="/releases" className="text-xs font-medium text-primary hover:underline">
              {t("releases_link")}
            </Link>
          </CardHeader>
          <CardContent className="space-y-3">
            {recentReleases.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("releases_empty")}</p>
            ) : (
              recentReleases.map((release) => (
                <Link
                  key={release.id}
                  href={`/releases/${release.id}`}
                  className="flex items-center justify-between gap-3 rounded-md border p-3 transition-colors hover:border-primary/40"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{release.appName}</p>
                    <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                      <GitBranch className="h-3 w-3" />
                      <span className="truncate">{release.gitRef}</span>
                      <span className="font-mono">{release.shortSha}</span>
                    </p>
                  </div>
                  <div className="shrink-0 text-right">
                    <StatusBadge status={release.status} />
                    <p className="mt-1 text-[10px] text-muted-foreground">
                      {formatDate(release.createdAt, locale)}
                    </p>
                  </div>
                </Link>
              ))
            )}
          </CardContent>
        </Card>
      </section>

      <section className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
            <CardTitle className="text-base">{t("endpoints_title")}</CardTitle>
            <Link href="/public-access" className="text-xs font-medium text-primary hover:underline">
              {t("endpoints_link")}
            </Link>
          </CardHeader>
          <CardContent className="space-y-3">
            {brokenEndpointRows.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("endpoints_empty")}</p>
            ) : (
              brokenEndpointRows.map((endpoint) => (
                <div key={endpoint.hostname} className="rounded-md border p-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <Link
                        href={endpoint.url}
                        target="_blank"
                        className="inline-flex max-w-full items-center gap-1 truncate text-sm font-medium hover:text-primary"
                      >
                        <ExternalLink className="h-3 w-3 shrink-0" />
                        <span className="truncate">{endpoint.hostname}</span>
                      </Link>
                      <p className="mt-1 truncate text-xs text-muted-foreground">
                        {[endpoint.appName, endpoint.tenantName, endpoint.environment].filter(Boolean).join(" / ") || t("endpoints_no_owner")}
                      </p>
                    </div>
                    <StatusBadge status={endpoint.healthStatus} />
                  </div>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {endpoint.issues.map((issue) => (
                      <Badge key={issue} variant="outline" className="font-mono text-[10px]">
                        {issue}
                      </Badge>
                    ))}
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
            <CardTitle className="text-base">{t("machines_title")}</CardTitle>
            <Link href="/vms" className="text-xs font-medium text-primary hover:underline">
              {t("machines_link")}
            </Link>
          </CardHeader>
          <CardContent className="space-y-3">
            {machineRows.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("machines_empty")}</p>
            ) : (
              machineRows.map((machine) => (
                <Link
                  key={machine.id}
                  href={`/vms/${machine.id}`}
                  className="flex items-center justify-between gap-3 rounded-md border p-3 transition-colors hover:border-primary/40"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{machine.name}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {t("machines_app_envs", { count: machine.appEnvironmentCount })}
                      {machine.previewAppEnvironmentCount > 0 ? ` / ${t("machines_previews", { count: machine.previewAppEnvironmentCount })}` : ""}
                    </p>
                    <p className="mt-1 truncate text-xs text-muted-foreground">
                      {machine.readinessReason}
                    </p>
                  </div>
                  <div className="shrink-0 text-right">
                    <StatusBadge status={machine.readinessStatus} />
                    <p className="mt-1 font-mono text-[10px] uppercase text-muted-foreground">
                      {machine.status}
                    </p>
                  </div>
                </Link>
              ))
            )}
          </CardContent>
        </Card>
      </section>

      {machines.length > 0 || disk ? (
        <section className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          {machines.length > 0 ? (
            <NodeDiskCard
              nodes={machines.map((m) => ({
                name: m.name,
                slug: m.slug,
                totalBytes: m.rootDiskTotalBytes,
                availableBytes: m.rootDiskAvailableBytes,
              }))}
            />
          ) : null}
          {disk ? <DiskUsageCard data={disk} /> : null}
        </section>
      ) : null}
    </div>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const variant = severity === "critical" ? "destructive" : severity === "warning" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{severity}</Badge>;
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant =
    normalized === "healthy" || normalized === "ready"
      ? "success"
      : normalized === "failed" || normalized === "broken" || normalized === "offline"
        ? "destructive"
        : normalized === "active" || normalized === "busy"
          ? "info"
          : normalized === "degraded" || normalized === "deploying"
            ? "warning"
            : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function formatDate(value: string | null, locale: string) {
  if (!value) return "-";
  return new Intl.DateTimeFormat(locale === "es" ? "es-CO" : "en-GB", {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}
