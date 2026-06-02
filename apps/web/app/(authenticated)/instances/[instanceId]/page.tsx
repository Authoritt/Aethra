import Link from "next/link";
import { notFound, redirect } from "next/navigation";
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

export const dynamic = "force-dynamic";

export default async function InstanceDetailPage({
  params,
}: {
  params: Promise<{ instanceId: string }>;
}) {
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
            Error cargando la instance.
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

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Template", href: `/templates/${instance.templateId}` },
          { label: "Client", href: `/clients/${instance.clientId}` },
          { label: instance.slug },
        ]}
        title={instance.slug}
        description={
          <>
            container <span className="font-mono">{instance.containerName}</span>
          </>
        }
        actions={
          openUrl ? (
            <Button asChild variant="outline">
              <a href={openUrl} target="_blank" rel="noreferrer noopener">
                <ExternalLink className="mr-2 h-4 w-4" />
                Abrir
              </a>
            </Button>
          ) : null
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">env: {instance.environment}</Badge>
        <Badge variant="outline" className="font-mono">
          vm: {instance.targetVmId.slice(0, 8)}
        </Badge>
        <Badge variant="outline">{instance.ports.length} ports</Badge>
        <Badge variant="outline">{instance.volumes.length} volumes</Badge>
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="config">Configuración</TabsTrigger>
          <TabsTrigger value="deployments">
            Deployments ({deployments.length})
          </TabsTrigger>
          <TabsTrigger value="builds">Builds disponibles</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Hostname & routing</CardTitle>
                <CardDescription>
                  Auto-hostname y custom domain efectivos.
                </CardDescription>
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
                <CardTitle className="text-base">Auto-deploy</CardTitle>
                <CardDescription>
                  Cuando está activo, cada nuevo build verde del template padre
                  dispara automáticamente un deploy aquí.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <AutoDeployToggle
                  instanceId={instance.id}
                  initial={instance.autoDeployOnNewBuild}
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
                  Ports
                </CardTitle>
              </CardHeader>
              <CardContent>
                {instance.ports.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Sin puertos.</p>
                ) : (
                  <ul className="flex flex-col gap-1 font-mono text-[11px]">
                    {instance.ports.map((p, i) => (
                      <li
                        key={i}
                        className="rounded border border-border bg-muted px-2 py-1"
                      >
                        {p.containerPort} → {p.hostPort ?? "auto"}{" "}
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
                  Volumes
                </CardTitle>
              </CardHeader>
              <CardContent>
                {instance.volumes.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Sin volumes.</p>
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
              title="Sin deployments"
              description="Esta instance aún no se ha desplegado."
            />
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Build</TableHead>
                    <TableHead>Creado</TableHead>
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
              title="Sin builds del template"
              description="Cuando haya uno verde podrás desplegarlo aquí."
            />
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Ref</TableHead>
                    <TableHead>SHA</TableHead>
                    <TableHead>Image</TableHead>
                    <TableHead className="text-right">Acción</TableHead>
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
