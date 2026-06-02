import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { GitBranch, Plus, Rocket } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  InstanceSummary,
  TemplateDetail,
} from "@/lib/types";
import { RotateWebhookSecretButton } from "./RotateWebhookSecretButton";

export const dynamic = "force-dynamic";

export default async function TemplateDetailPage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const { templateId } = await params;

  const [templateResult, instancesResult, buildsResult] = await Promise.all([
    serverFetch<TemplateDetail>(`/api/templates/${templateId}`),
    serverFetch<InstanceSummary[]>(`/api/templates/${templateId}/instances`),
    serverFetch<BuildSummary[]>(`/api/builds/templates/${templateId}`),
  ]);

  if (templateResult === "unauthorized") redirect("/login");
  if (templateResult === "notfound") notFound();
  if (templateResult === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el template.
          </CardContent>
        </Card>
      </div>
    );
  }

  const template = templateResult;
  const instances = Array.isArray(instancesResult) ? instancesResult : [];
  const builds = Array.isArray(buildsResult) ? buildsResult.slice(0, 10) : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Proyectos", href: "/projects" },
          { label: "Proyecto", href: `/projects/${template.projectId}` },
          { label: template.name },
        ]}
        title={template.name}
        description={
          <>
            <span className="font-mono text-xs">{template.slug}</span>
            {template.description ? (
              <>
                <span className="mx-2 text-muted-foreground/50">·</span>
                {template.description}
              </>
            ) : null}
          </>
        }
        actions={
          <>
            <RotateWebhookSecretButton templateId={template.id} />
            <Button asChild>
              <Link href={`/templates/${template.id}/instances/new`}>
                <Plus className="mr-2 h-4 w-4" />
                Crear instance
              </Link>
            </Button>
          </>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">build: {template.build.buildType}</Badge>
        <Badge variant="outline">{template.instanceCount} instances</Badge>
        <Badge variant="outline">
          <GitBranch className="mr-1 h-3 w-3" />
          {template.source.branch}
        </Badge>
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="instances">
            Instances ({instances.length})
          </TabsTrigger>
          <TabsTrigger value="builds">
            Builds ({builds.length})
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  Source
                </CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="flex flex-col gap-3 text-sm">
                  <Kv label="Git repo" value={template.source.gitRepoUrl} mono />
                  <Kv label="Branch" value={template.source.branch} mono />
                  <Kv
                    label="Base directory"
                    value={template.source.baseDirectory}
                    mono
                  />
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      Watch paths
                    </dt>
                    <dd className="mt-1 flex flex-wrap gap-1">
                      {template.source.watchPaths.map((p) => (
                        <span
                          key={p}
                          className="rounded border border-border bg-muted px-2 py-0.5 font-mono text-[10px] text-foreground"
                        >
                          {p}
                        </span>
                      ))}
                    </dd>
                  </div>
                </dl>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  Build
                </CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="flex flex-col gap-3 text-sm">
                  <Kv label="Tipo" value={template.build.buildType} />
                  {template.build.dockerfilePath ? (
                    <Kv
                      label="Dockerfile"
                      value={template.build.dockerfilePath}
                      mono
                    />
                  ) : null}
                  {template.build.composeFilePath ? (
                    <Kv
                      label="Compose file"
                      value={template.build.composeFilePath}
                      mono
                    />
                  ) : null}
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      Build args
                    </dt>
                    <dd className="mt-1">
                      {template.build.buildArgs.length === 0 ? (
                        <span className="text-xs text-muted-foreground">
                          sin args
                        </span>
                      ) : (
                        <ul className="flex flex-col gap-1 font-mono text-[11px]">
                          {template.build.buildArgs.map((a) => (
                            <li
                              key={a.key}
                              className="rounded border border-border bg-muted px-2 py-1"
                            >
                              <span className="text-primary">{a.key}</span>
                              <span className="text-muted-foreground">=</span>
                              <span className="text-foreground">{a.value}</span>
                            </li>
                          ))}
                        </ul>
                      )}
                    </dd>
                  </div>
                </dl>
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="instances" className="mt-6">
          {instances.length === 0 ? (
            <EmptyState
              icon={Rocket}
              title="Sin instances"
              description="Creá la primera para desplegar este template hacia un client + environment."
              action={
                <Button asChild>
                  <Link href={`/templates/${template.id}/instances/new`}>
                    <Plus className="mr-2 h-4 w-4" />
                    Crear instance
                  </Link>
                </Button>
              }
            />
          ) : (
            <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
              {instances.map((inst) => (
                <li key={inst.id}>
                  <Link
                    href={`/instances/${inst.id}`}
                    className="group block rounded-md border border-border bg-card p-4 transition-colors hover:border-primary/40"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <h3 className="truncate font-mono text-xs text-foreground">
                        {inst.slug}
                      </h3>
                      <Badge variant="outline">{inst.environment}</Badge>
                    </div>
                    <p className="mt-2 truncate font-mono text-[11px] text-muted-foreground">
                      {inst.customDomain ?? inst.autoHostname ?? "sin hostname"}
                    </p>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="builds" className="mt-6">
          {builds.length === 0 ? (
            <EmptyState
              title="Sin builds aún"
              description="Cuando dispares un webhook o build manual, los últimos 10 aparecerán aquí."
            />
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Ref</TableHead>
                    <TableHead>SHA</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Creado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {builds.map((b) => (
                    <TableRow key={b.id}>
                      <TableCell>
                        <Link href={`/builds/${b.id}`}>
                          <BuildStatusPill status={b.status} />
                        </Link>
                      </TableCell>
                      <TableCell>
                        <Link
                          href={`/builds/${b.id}`}
                          className="font-mono text-xs hover:text-primary"
                        >
                          {b.gitRef}
                        </Link>
                      </TableCell>
                      <TableCell className="font-mono text-[11px] text-muted-foreground">
                        {b.gitSha.slice(0, 8)}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {b.trigger}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {formatDate(b.createdAt)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Card>
          )}
        </TabsContent>
      </Tabs>
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
      <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd
        className={`mt-0.5 break-all text-foreground ${mono ? "font-mono text-xs" : "text-sm"}`}
      >
        {value}
      </dd>
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
