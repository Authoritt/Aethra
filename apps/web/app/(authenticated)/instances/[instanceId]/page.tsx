import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ExternalLink } from "lucide-react";
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
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  DeploymentSummary,
  InstanceDetail,
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

  const [deploymentsResult, templateResult] = await Promise.all([
    serverFetch<DeploymentSummary[]>(
      `/api/deployments/instances/${instance.id}`,
    ),
    serverFetch<TemplateDetail>(`/api/templates/${instance.templateId}`),
  ]);

  const deployments = Array.isArray(deploymentsResult)
    ? deploymentsResult.slice(0, 10)
    : [];

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

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("templates"), href: `/templates/${instance.templateId}` },
          { label: tBreadcrumbs("clients"), href: `/clients/${instance.clientId}` },
          { label: instance.slug },
        ]}
        title={instance.slug}
        description={
          <>
            {t("container_label", { name: "" })}
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
