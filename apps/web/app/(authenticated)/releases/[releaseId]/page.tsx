import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import {
  AlertTriangle,
  Boxes,
  ExternalLink,
  Rocket,
  Server,
  Timer,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
import { serverFetch } from "@/lib/server-fetch";
import type {
  AppEnvironmentOverviewDto,
  MachineOverviewDto,
  OperationalIssueDto,
  PublicEndpointOverviewDto,
  ReleaseOverviewDto,
  ReleaseTargetDto,
} from "@/lib/types";
import { ReleaseTargetActions } from "./ReleaseTargetActions";

export const dynamic = "force-dynamic";

export default async function ReleaseDetailPage({
  params,
}: {
  params: Promise<{ releaseId: string }>;
}) {
  const { releaseId } = await params;
  const [releaseData, envsData, endpointsData, issuesData, machinesData] =
    await Promise.all([
      serverFetch<ReleaseOverviewDto>(`/api/ops/releases/${releaseId}`),
      serverFetch<AppEnvironmentOverviewDto[]>("/api/ops/app-environments"),
      serverFetch<PublicEndpointOverviewDto[]>("/api/ops/public-endpoints"),
      serverFetch<OperationalIssueDto[]>("/api/ops/operational-issues"),
      serverFetch<MachineOverviewDto[]>("/api/ops/machines"),
    ]);

  if (
    releaseData === "unauthorized" ||
    envsData === "unauthorized" ||
    endpointsData === "unauthorized" ||
    issuesData === "unauthorized" ||
    machinesData === "unauthorized"
  ) {
    redirect("/login");
  }
  if (releaseData === "notfound") {
    notFound();
  }
  if (releaseData === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el release.
          </CardContent>
        </Card>
      </div>
    );
  }
  const release = releaseData;

  const envs = Array.isArray(envsData) ? envsData : [];
  const endpoints = Array.isArray(endpointsData) ? endpointsData : [];
  const issues = Array.isArray(issuesData) ? issuesData : [];
  const machines = Array.isArray(machinesData) ? machinesData : [];

  const targetRows = release.targets.map((target) => {
    const env = envs.find((x) => x.id === target.appEnvironmentId) ?? null;
    const machine = env ? machines.find((m) => m.id === env.machineId) ?? null : null;
    const endpoint = endpoints.find((e) => e.appEnvironmentId === target.appEnvironmentId) ?? null;
    const targetIssues = issues.filter((i) => i.appEnvironmentId === target.appEnvironmentId);
    return { target, env, machine, endpoint, issues: targetIssues };
  });
  const issueCount = targetRows.reduce((sum, row) => sum + row.issues.length, 0);
  const duration = release.startedAt && release.finishedAt
    ? formatDuration(new Date(release.finishedAt).getTime() - new Date(release.startedAt).getTime())
    : null;

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Releases", href: "/releases" },
          release.appId
            ? { label: release.appName, href: `/apps/${release.appId}` }
            : { label: release.appName },
          { label: release.shortSha || release.id.slice(0, 8) },
        ]}
        title={`${release.appName} release`}
        description={
          <span className="font-mono text-xs text-muted-foreground">
            {release.gitRef} / {release.gitSha}
          </span>
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={release.status} />
            <Button asChild variant="outline">
              <Link href={`/builds/${release.buildId}`}>
                <Boxes className="mr-2 h-4 w-4" />
                Build tecnico
              </Link>
            </Button>
          </div>
        }
      />

      <section className="grid grid-cols-2 gap-4 lg:grid-cols-5">
        <KpiCard
          label="Targets"
          value={release.targetCount}
          icon={<Rocket className="h-4 w-4" />}
        />
        <KpiCard
          label="Completed"
          value={release.completedCount}
          tone={release.completedCount > 0 ? "success" : "default"}
          icon={<Rocket className="h-4 w-4" />}
        />
        <KpiCard
          label="Failed"
          value={release.failedCount}
          tone={release.failedCount > 0 ? "destructive" : "success"}
          icon={<AlertTriangle className="h-4 w-4" />}
        />
        <KpiCard
          label="Active"
          value={release.activeCount}
          tone={release.activeCount > 0 ? "info" : "default"}
          icon={<Timer className="h-4 w-4" />}
        />
        <KpiCard
          label="Issues"
          value={issueCount}
          tone={issueCount > 0 ? "warning" : "success"}
          icon={<AlertTriangle className="h-4 w-4" />}
        />
      </section>

      <section className="grid grid-cols-1 gap-4 xl:grid-cols-[0.85fr_1.15fr]">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Git y artefacto</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <Kv label="Ref" value={release.gitRef} mono />
            <Kv label="SHA" value={release.gitSha} mono />
            <Kv label="Trigger" value={release.trigger} />
            <Kv label="Triggered by" value={release.triggeredBy ?? "-"} />
            <Kv label="Image" value={release.imageRef ?? "-"} mono />
            {release.errorCode || release.errorMessage ? (
              <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3">
                <p className="text-xs font-medium uppercase tracking-wider text-destructive">
                  Build error
                </p>
                <p className="mt-1 font-mono text-xs text-destructive">
                  {release.errorCode ?? "release.error"}
                </p>
                <p className="mt-1 text-sm text-destructive">
                  {release.errorMessage}
                </p>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Timeline</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <TimelineItem label="Created" value={formatDate(release.createdAt)} />
            <TimelineItem label="Started" value={release.startedAt ? formatDate(release.startedAt) : "-"} />
            <TimelineItem label="Finished" value={release.finishedAt ? formatDate(release.finishedAt) : "-"} />
            <div className="sm:col-span-3">
              <Badge variant="outline">
                Duration {duration ?? "not finished"}
              </Badge>
            </div>
          </CardContent>
        </Card>
      </section>

      <Card>
        <CardHeader className="flex-row items-center justify-between gap-4 space-y-0">
          <CardTitle className="text-base">Fan-out por App Environment</CardTitle>
          <Badge variant="outline">{targetRows.length} targets</Badge>
        </CardHeader>
        <CardContent>
          {targetRows.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Este release aun no tiene deployments asociados.
            </p>
          ) : (
            <Table>
              <TableHeader>
                  <TableRow>
                  <TableHead>Target</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Machine</TableHead>
                  <TableHead>Public access</TableHead>
                  <TableHead>Issues</TableHead>
                  <TableHead className="text-right">Accion</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {targetRows.map((row) => (
                  <TableRow key={row.target.deploymentId}>
                    <TableCell>
                      <TargetCell target={row.target} env={row.env} />
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={row.target.status} />
                    </TableCell>
                    <TableCell>
                      {row.machine ? (
                        <Link href={`/vms/${row.machine.id}`} className="inline-flex items-center gap-1 text-sm hover:text-primary">
                          <Server className="h-3.5 w-3.5" />
                          {row.machine.name}
                        </Link>
                      ) : (
                        <span className="text-sm text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {row.endpoint ? (
                        <Link href={row.endpoint.url} target="_blank" className="inline-flex max-w-56 items-center gap-1 truncate text-sm text-primary">
                          <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                          <span className="truncate">{row.endpoint.hostname}</span>
                        </Link>
                      ) : row.env?.publicUrl ? (
                        <Link href={row.env.publicUrl} target="_blank" className="inline-flex max-w-56 items-center gap-1 truncate text-sm text-primary">
                          <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                          <span className="truncate">{row.env.publicUrl.replace(/^https?:\/\//, "")}</span>
                        </Link>
                      ) : (
                        <span className="text-sm text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {row.issues.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                          {row.issues.slice(0, 2).map((issue) => (
                            <Badge key={issue.id} variant={issue.severity === "critical" ? "destructive" : "warning"} className="font-mono text-[10px]">
                              {issue.code}
                            </Badge>
                          ))}
                          {row.issues.length > 2 ? (
                            <Badge variant="outline">+{row.issues.length - 2}</Badge>
                          ) : null}
                        </div>
                      ) : (
                        <Badge variant="success">clean</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-2">
                        <Button asChild size="sm" variant="ghost">
                          <Link href={`/deployments/${row.target.deploymentId}`}>
                            Deployment
                          </Link>
                        </Button>
                        <ReleaseTargetActions
                          buildId={release.buildId}
                          deploymentId={row.target.deploymentId}
                          instanceId={row.target.appEnvironmentId}
                          status={row.target.status}
                        />
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function TargetCell({
  target,
  env,
}: {
  target: ReleaseTargetDto;
  env: AppEnvironmentOverviewDto | null;
}) {
  return (
    <div className="min-w-0">
      <Link href={`/instances/${target.appEnvironmentId}`} className="block truncate font-medium hover:text-primary">
        {env?.appName ?? target.appEnvironmentSlug}
      </Link>
      <p className="mt-1 text-xs text-muted-foreground">
        {target.tenantName} / {target.environment}
      </p>
    </div>
  );
}

function TimelineItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border bg-muted/20 p-3">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 text-sm text-foreground">{value}</p>
    </div>
  );
}

function Kv({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div>
      <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className={`mt-1 break-all text-sm text-foreground ${mono ? "font-mono text-xs" : ""}`}>
        {value}
      </p>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant =
    normalized === "healthy" || normalized === "completed" || normalized === "succeeded"
      ? "success"
      : normalized === "failed" || normalized === "rolledback" || normalized === "error"
        ? "destructive"
        : normalized === "active" || normalized === "pending" || normalized === "pulling" || normalized === "starting"
          ? "info"
          : normalized === "degraded" || normalized === "healthcheck" || normalized === "swapping"
            ? "warning"
            : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("es-CO", {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatDuration(ms: number) {
  if (!Number.isFinite(ms) || ms < 0) return "-";
  if (ms < 1000) return `${ms} ms`;
  const seconds = Math.floor(ms / 1000);
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  return minutes > 0 ? `${minutes}m ${remainingSeconds}s` : `${remainingSeconds}s`;
}
