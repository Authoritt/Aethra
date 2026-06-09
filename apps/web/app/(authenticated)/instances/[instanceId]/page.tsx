import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AlertTriangle, ExternalLink, GitBranch, MonitorCheck, Network, Rocket, Server } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { PageHeader } from "@/components/layout/page-header";
import { BuildStatusPill } from "@/components/aethra/build-status-pill";
import { DeploymentStatusPill } from "@/components/aethra/deployment-status-pill";
import { PublicAccessReconcileActions } from "@/components/aethra/PublicAccessReconcileActions";
import { ScopedEnvVarsPanel } from "@/components/aethra/ScopedEnvVarsPanel";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { serverFetch } from "@/lib/server-fetch";
import type {
  AppEnvironmentOverviewDto,
  BuildSummary,
  DataServiceOverviewDto,
  DeploymentSummary,
  InstanceDetail,
  MachineOverviewDto,
  OperationalIssueDto,
  PublicAccessStateDto,
  PublicEndpointOverviewDto,
  ReleaseOverviewDto,
  TemplateDetail,
} from "@/lib/types";
import { AutoDeployToggle } from "./AutoDeployToggle";
import { CustomDomainForm } from "./CustomDomainForm";
import { DeployBuildButton } from "./DeployBuildButton";
import { DeployNativeButton } from "./DeployNativeButton";
import { TrackedRefEditor, DeleteInstanceButton } from "./InstanceAdmin";

export const dynamic = "force-dynamic";

export default async function InstanceDetailPage({
  params,
}: {
  params: Promise<{ instanceId: string }>;
}) {
  const t = await getTranslations("pages.instances_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { instanceId } = await params;

  const instanceResult = await serverFetch<InstanceDetail>(
    `/api/instances/${instanceId}`,
  );
  if (instanceResult === "unauthorized") redirect("/login");
  if (instanceResult === "notfound") notFound();
  if (instanceResult === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }
  const instance = instanceResult;

  const [
    deploymentsResult,
    templateResult,
    appEnvironmentsResult,
    releasesResult,
    publicEndpointsResult,
    publicAccessStatesResult,
    operationalIssuesResult,
    machinesResult,
    dataServicesResult,
  ] = await Promise.all([
    serverFetch<DeploymentSummary[]>(
      `/api/deployments/instances/${instance.id}`,
    ),
    serverFetch<TemplateDetail>(`/api/templates/${instance.templateId}`),
    serverFetch<AppEnvironmentOverviewDto[]>("/api/ops/app-environments"),
    serverFetch<ReleaseOverviewDto[]>("/api/ops/releases"),
    serverFetch<PublicEndpointOverviewDto[]>("/api/ops/public-endpoints"),
    serverFetch<PublicAccessStateDto[]>(`/api/ops/public-access-states?appEnvironmentId=${encodeURIComponent(instance.id)}`),
    serverFetch<OperationalIssueDto[]>("/api/ops/operational-issues"),
    serverFetch<MachineOverviewDto[]>("/api/ops/machines"),
    serverFetch<DataServiceOverviewDto[]>(`/api/ops/data-services?appEnvironmentId=${encodeURIComponent(instance.id)}`),
  ]);

  const deployments = Array.isArray(deploymentsResult)
    ? deploymentsResult.slice(0, 10)
    : [];
  const appEnvironments = Array.isArray(appEnvironmentsResult) ? appEnvironmentsResult : [];
  const releases = Array.isArray(releasesResult) ? releasesResult : [];
  const publicEndpoints = Array.isArray(publicEndpointsResult) ? publicEndpointsResult : [];
  const publicAccessState = Array.isArray(publicAccessStatesResult) ? publicAccessStatesResult[0] ?? null : null;
  const operationalIssues = Array.isArray(operationalIssuesResult) ? operationalIssuesResult : [];
  const machines = Array.isArray(machinesResult) ? machinesResult : [];
  const dataServices = Array.isArray(dataServicesResult) ? dataServicesResult : [];
  const operationalEnv = appEnvironments.find((env) => env.id === instance.id) ?? null;
  const envReleases = releases.filter((release) =>
    release.targets.some((target) => target.appEnvironmentId === instance.id),
  );
  const currentRelease = envReleases[0] ?? null;
  const envEndpoints = publicEndpoints.filter((endpoint) => endpoint.appEnvironmentId === instance.id);
  const envIssues = operationalIssues.filter((issue) => issue.appEnvironmentId === instance.id);
  const machine = machines.find((m) => m.id === instance.targetVmId) ?? null;

  let buildsResult:
    | Awaited<ReturnType<typeof serverFetch<BuildSummary[]>>>
    | null = null;
  if (
    templateResult !== "unauthorized" &&
    templateResult !== "notfound" &&
    templateResult !== "error"
  ) {
    buildsResult = await serverFetch<BuildSummary[]>(
      `/api/builds/templates/${instance.templateId}`,
    );
  }
  const builds = Array.isArray(buildsResult) ? buildsResult.slice(0, 10) : [];

  const effectiveHost = instance.customDomain ?? instance.autoHostname;
  const openUrl = effectiveHost ? `https://${effectiveHost}` : null;

  const template =
    templateResult !== "unauthorized" &&
    templateResult !== "notfound" &&
    templateResult !== "error"
      ? templateResult
      : null;
  const hasServices = (template?.services?.length ?? 0) > 0;
  const pageTitle = operationalEnv
    ? `${operationalEnv.appName} / ${operationalEnv.tenantName} / ${operationalEnv.environment}`
    : instance.slug;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          operationalEnv
            ? { label: "Apps", href: "/apps" }
            : { label: tBreadcrumbs("templates"), href: `/templates/${instance.templateId}` },
          operationalEnv
            ? { label: operationalEnv.appName, href: `/apps/${operationalEnv.appId}` }
            : { label: tBreadcrumbs("clients"), href: `/clients/${instance.clientId}` },
          { label: "App Environment" },
        ]}
        title={pageTitle}
        description={
          <>
            App Environment operativo. {t("container_label", { name: "" })}
            <span className="font-mono">{instance.containerName}</span>
          </>
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {hasServices ? (
              <DeployNativeButton instanceId={instance.id} hostname={effectiveHost} />
            ) : null}
            {openUrl ? (
              <Button asChild variant="outline">
                <a href={openUrl} target="_blank" rel="noreferrer noopener">
                  <ExternalLink className="mr-2 h-4 w-4" />
                  {t("open")}
                </a>
              </Button>
            ) : null}
            <DeleteInstanceButton instanceId={instance.id} slug={instance.slug} />
          </div>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">{t("env_badge", { env: instance.environment })}</Badge>
        <Badge variant="outline" className="font-mono">
          {t("vm_badge", { id: instance.targetVmId.slice(0, 8) })}
        </Badge>
        <Badge variant="outline">{t("ports_badge", { count: instance.ports.length })}</Badge>
        <Badge variant="outline">{t("volumes_badge", { count: instance.volumes.length })}</Badge>
        {instance.isEphemeral ? <Badge variant="warning">ephemeral</Badge> : null}
        {instance.effectiveTrackedRef ? (
          <Badge variant="outline" className="font-mono text-[10px]">
            {instance.effectiveTrackedRef}
          </Badge>
        ) : null}
      </div>

      <section className="mb-6 grid grid-cols-1 gap-4 lg:grid-cols-4">
        <Card>
          <CardContent className="space-y-3 p-5">
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Health
              </p>
              <MonitorCheck className="h-4 w-4 text-muted-foreground" />
            </div>
            <StatusBadge status={operationalEnv?.healthStatus ?? "unknown"} />
            <p className="text-xs text-muted-foreground">
              {envIssues.length === 0 ? "Sin issues operacionales activos." : `${envIssues.length} issue(s) requieren atencion.`}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="space-y-3 p-5">
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Current release
              </p>
              <Rocket className="h-4 w-4 text-muted-foreground" />
            </div>
            {currentRelease ? (
              <>
                <Link href={`/releases/${currentRelease.id}`} className="block font-mono text-sm font-medium hover:text-primary">
                  {currentRelease.shortSha || currentRelease.buildId.slice(0, 8)}
                </Link>
                <p className="flex min-w-0 items-center gap-1 text-xs text-muted-foreground">
                  <GitBranch className="h-3 w-3 shrink-0" />
                  <span className="truncate">{currentRelease.gitRef}</span>
                </p>
              </>
            ) : (
              <p className="text-sm text-muted-foreground">Sin release asociado.</p>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent className="space-y-3 p-5">
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Public access
              </p>
              <Network className="h-4 w-4 text-muted-foreground" />
            </div>
            {envEndpoints.length > 0 ? (
              <div className="space-y-2">
                {envEndpoints.slice(0, 2).map((endpoint) => (
                  <div key={endpoint.hostname} className="flex items-center justify-between gap-2">
                    <Link href={endpoint.url} target="_blank" className="min-w-0 truncate text-sm font-medium hover:text-primary">
                      {endpoint.hostname}
                    </Link>
                    <StatusBadge status={endpoint.healthStatus} />
                  </div>
                ))}
              </div>
            ) : openUrl ? (
              <Link href={openUrl} target="_blank" className="block truncate text-sm font-medium hover:text-primary">
                {effectiveHost}
              </Link>
            ) : (
              <p className="text-sm text-muted-foreground">Sin URL publica.</p>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent className="space-y-3 p-5">
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Machine
              </p>
              <Server className="h-4 w-4 text-muted-foreground" />
            </div>
            <Link href={`/vms/${instance.targetVmId}`} className="block truncate text-sm font-medium hover:text-primary">
              {machine?.name ?? operationalEnv?.machineName ?? instance.targetVmId}
            </Link>
            <StatusBadge status={machine?.readinessStatus ?? operationalEnv?.machineStatus ?? "unknown"} />
          </CardContent>
        </Card>
      </section>

      {envIssues.length > 0 ? (
        <Card className="mb-6 border-warning/40 bg-warning/5">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertTriangle className="h-4 w-4" />
              Operational issues
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-2">
            {envIssues.map((issue) => (
              <Badge key={issue.id} variant={issue.severity === "critical" ? "destructive" : "warning"} className="font-mono text-[10px]">
                {issue.code}
              </Badge>
            ))}
          </CardContent>
        </Card>
      ) : null}

      <Card className="mb-6">
        <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
          <div>
            <CardTitle className="text-base">Data Services</CardTitle>
            <CardDescription>
              Servicios consumidos por este App Environment.
            </CardDescription>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link href="/services">Ver servicios</Link>
          </Button>
        </CardHeader>
        <CardContent>
          {dataServices.length === 0 ? (
            <p className="rounded-md border border-dashed p-3 text-sm text-muted-foreground">
              Este App Environment no tiene data services vinculados.
            </p>
          ) : (
            <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
              {dataServices.map((service) => {
                const binding = service.bindings.find((b) => b.appEnvironmentId === instance.id);
                return (
                  <Link
                    key={service.id}
                    href={`/services/${service.id}`}
                    className="rounded-md border p-4 transition-colors hover:border-primary/40"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium">{service.name}</p>
                        <p className="mt-1 font-mono text-xs text-muted-foreground">
                          {service.type} / {service.version}
                        </p>
                      </div>
                      <StatusBadge status={service.status} />
                    </div>
                    <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
                      <SmallStat label="Resource" value={binding?.resourceName ?? "-"} mono />
                      <SmallStat label="Permissions" value={binding?.permissions ?? "-"} />
                      <SmallStat label="Env prefix" value={binding?.envVarPrefix || "-"} mono />
                      <SmallStat label="Binding" value={binding?.status ?? "-"} />
                    </div>
                    {service.lastBackupAt ? (
                      <p className="mt-3 text-xs text-muted-foreground">
                        Last backup {formatDate(service.lastBackupAt)}
                      </p>
                    ) : null}
                  </Link>
                );
              })}
            </div>
          )}
        </CardContent>
      </Card>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">{t("tab_overview")}</TabsTrigger>
          <TabsTrigger value="config">{t("tab_config")}</TabsTrigger>
          <TabsTrigger value="deployments">
            {t("tab_deployments", { count: deployments.length })}
          </TabsTrigger>
          <TabsTrigger value="builds">{t("tab_builds")}</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">{t("hostname_routing_title")}</CardTitle>
                <CardDescription>{t("hostname_routing_description")}</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <AutoHostnameInfo
                  autoHostname={instance.autoHostname}
                  customDomain={instance.customDomain}
                />
                <CustomDomainForm
                  instanceId={instance.id}
                  initialDomain={instance.customDomain}
                />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Public Access state</CardTitle>
                <CardDescription>
                  Estado deseado vs. DNS, Tunnel, Route, TLS y Monitor.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {publicAccessState ? (
                  <>
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="truncate font-mono text-sm">
                          {publicAccessState.desiredHostname ?? "-"}
                        </p>
                        <p className="text-xs text-muted-foreground">
                          source {publicAccessState.desiredSource}
                        </p>
                      </div>
                      <StatusBadge status={publicAccessState.healthStatus} />
                    </div>
                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-5">
                      <ChecklistItem label="DNS" ok={publicAccessState.dnsConfigured} />
                      <ChecklistItem
                        label={`Tunnel${publicAccessState.tunnelName ? ` ${publicAccessState.tunnelName}` : ""}`}
                        ok={publicAccessState.tunnelConfigured}
                      />
                      <ChecklistItem label="Route" ok={publicAccessState.routeConfigured} />
                      <ChecklistItem label="TLS" ok={publicAccessState.tlsEnabled} />
                      <ChecklistItem
                        label={`Monitor${publicAccessState.monitorStatus ? ` ${publicAccessState.monitorStatus}` : ""}`}
                        ok={publicAccessState.monitorConfigured && publicAccessState.monitorStatus !== "Down"}
                      />
                    </div>
                    <div className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                      <SmallStat
                        label="DNS target"
                        value={publicAccessState.dnsTarget ?? "-"}
                        mono
                      />
                      <SmallStat
                        label="Expected target"
                        value={publicAccessState.expectedDnsTarget ?? "-"}
                        mono
                      />
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <Badge variant="outline" className="font-mono text-[10px]">
                        next {publicAccessState.nextAction}
                      </Badge>
                      {publicAccessState.issues.map((issue) => (
                        <Badge key={issue} variant="warning" className="font-mono text-[10px]">
                          {issue}
                        </Badge>
                      ))}
                    </div>
                    <PublicAccessReconcileActions
                      appEnvironmentId={instance.id}
                      disabled={publicAccessState.nextAction === "set_hostname"}
                    />
                  </>
                ) : (
                  <p className="rounded-md border border-dashed p-3 text-sm text-muted-foreground">
                    No hay estado de Public Access para este ambiente.
                  </p>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">{t("autodeploy_title")}</CardTitle>
                <CardDescription>{t("autodeploy_description")}</CardDescription>
              </CardHeader>
              <CardContent>
                <AutoDeployToggle
                  instanceId={instance.id}
                  initial={instance.autoDeployOnNewBuild}
                />
              </CardContent>
            </Card>
            <Card className="md:col-span-2">
              <CardHeader>
                <CardTitle className="text-base">Rama (tracked-ref)</CardTitle>
                <CardDescription>Qué rama del repo despliega esta instancia.</CardDescription>
              </CardHeader>
              <CardContent>
                <TrackedRefEditor
                  instanceId={instance.id}
                  trackedRef={instance.trackedRef ?? null}
                  effectiveTrackedRef={instance.effectiveTrackedRef ?? null}
                />
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="config" className="mt-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  {t("ports_title")}
                </CardTitle>
              </CardHeader>
              <CardContent>
                {instance.ports.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t("no_ports")}</p>
                ) : (
                  <ul className="flex flex-col gap-1 font-mono text-[11px]">
                    {instance.ports.map((p, i) => (
                      <li
                        key={i}
                        className="rounded border border-border bg-muted px-2 py-1"
                      >
                        {p.containerPort} → {p.hostPort ?? t("auto_port")}{" "}
                        <span className="text-muted-foreground">
                          /{p.protocol.toLowerCase()}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  {t("volumes_title")}
                </CardTitle>
              </CardHeader>
              <CardContent>
                {instance.volumes.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t("no_volumes")}</p>
                ) : (
                  <ul className="flex flex-col gap-1 font-mono text-[11px]">
                    {instance.volumes.map((v, i) => (
                      <li
                        key={i}
                        className="rounded border border-border bg-muted px-2 py-1"
                      >
                        <span className="text-primary">{v.name}</span>
                        <span className="text-muted-foreground"> → </span>
                        {v.containerPath}
                        {v.readOnly ? (
                          <span className="ml-2 text-[10px] uppercase tracking-wider text-muted-foreground">
                            ro
                          </span>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
          </div>
          <div className="mt-4">
            <ScopedEnvVarsPanel scopeType="instance" scopeId={instance.id} />
          </div>
        </TabsContent>

        <TabsContent value="deployments" className="mt-6">
          {deployments.length === 0 ? (
            <EmptyState
              title={t("deployments_empty_title")}
              description={t("deployments_empty_description")}
            />
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("col_status")}</TableHead>
                    <TableHead>{t("col_trigger")}</TableHead>
                    <TableHead>{t("col_build")}</TableHead>
                    <TableHead>{t("col_created")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {deployments.map((d) => (
                    <TableRow key={d.id}>
                      <TableCell>
                        <Link href={`/deployments/${d.id}`}>
                          <DeploymentStatusPill status={d.status} />
                        </Link>
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {d.trigger}
                      </TableCell>
                      <TableCell>
                        <Link
                          href={`/builds/${d.buildId}`}
                          className="font-mono text-[11px] hover:text-primary"
                        >
                          {d.buildId.slice(0, 8)}
                        </Link>
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {formatDate(d.createdAt)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Card>
          )}
        </TabsContent>

        <TabsContent value="builds" className="mt-6">
          {builds.length === 0 ? (
            <EmptyState
              title={t("builds_empty_title")}
              description={t("builds_empty_description")}
            />
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("col_status")}</TableHead>
                    <TableHead>{t("col_ref")}</TableHead>
                    <TableHead>{t("col_sha")}</TableHead>
                    <TableHead>{t("col_image")}</TableHead>
                    <TableHead className="text-right">{t("col_action")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {builds.map((b) => {
                    const deployable =
                      Boolean(b.imageRef) && b.status === "Completed";
                    return (
                      <TableRow key={b.id}>
                        <TableCell>
                          <Link href={`/builds/${b.id}`}>
                            <BuildStatusPill status={b.status} />
                          </Link>
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {b.gitRef}
                        </TableCell>
                        <TableCell className="font-mono text-[11px] text-muted-foreground">
                          {b.gitSha.slice(0, 8)}
                        </TableCell>
                        <TableCell className="font-mono text-[11px] text-muted-foreground">
                          {b.imageRef ?? "—"}
                        </TableCell>
                        <TableCell className="text-right">
                          <DeployBuildButton
                            buildId={b.id}
                            instanceId={instance.id}
                            disabled={!deployable}
                          />
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Card>
          )}
        </TabsContent>
      </Tabs>
    </div>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant =
    normalized === "healthy" || normalized === "ready" || normalized === "completed" || normalized === "connected"
      ? "success"
      : normalized === "failed" || normalized === "broken" || normalized === "offline" || normalized === "disconnected"
        ? "destructive"
        : normalized === "active" || normalized === "busy"
          ? "info"
          : normalized === "degraded" || normalized === "deploying" || normalized === "warning"
            ? "warning"
            : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function SmallStat({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="min-w-0">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className={`mt-0.5 truncate text-foreground ${mono ? "font-mono" : ""}`} title={value}>
        {value}
      </p>
    </div>
  );
}

function ChecklistItem({ label, ok }: { label: string; ok: boolean }) {
  return (
    <div className="rounded-md border bg-muted/20 p-3">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <Badge variant={ok ? "success" : "warning"} className="mt-2 font-mono text-[10px] uppercase">
        {ok ? "ok" : "pending"}
      </Badge>
    </div>
  );
}
