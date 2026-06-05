import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { GitBranch, Pencil, Plus, Rocket } from "lucide-react";
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
import { ServicesEditor } from "./ServicesEditor";
import { EnvironmentMappingEditor, AutoPreviewToggle, DeleteTemplateButton } from "./TemplateConfigEditors";

export const dynamic = "force-dynamic";

export default async function TemplateDetailPage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const t = await getTranslations("pages.templates_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const tCommon = await getTranslations("common");
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
            {t("load_error")}
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
          { label: tBreadcrumbs("projects"), href: "/projects" },
          { label: tCommon("go_to_project"), href: `/projects/${template.projectId}` },
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
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="outline" size="sm">
              <Link href={`/templates/${template.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                Editar
              </Link>
            </Button>
            <RotateWebhookSecretButton templateId={template.id} />
            <DeleteTemplateButton templateId={template.id} name={template.name} />
            <Button asChild>
              <Link href={`/templates/${template.id}/instances/new`}>
                <Plus className="mr-2 h-4 w-4" />
                {t("create_instance")}
              </Link>
            </Button>
          </div>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">
          {t("build_badge", { type: template.buildType })}
        </Badge>
        <Badge variant="outline">
          {t("instances_badge", { count: template.instanceCount ?? 0 })}
        </Badge>
        <Badge variant="outline">
          <GitBranch className="mr-1 h-3 w-3" />
          {template.branch}
        </Badge>
        {template.autoPreviewPullRequests ? (
          <Badge variant="warning">PR previews on</Badge>
        ) : null}
        {template.environmentMapping?.length > 0 ? (
          <Badge variant="outline">{template.environmentMapping.length} env mappings</Badge>
        ) : null}
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">{t("tab_overview")}</TabsTrigger>
          <TabsTrigger value="instances">
            {t("tab_instances", { count: instances.length })}
          </TabsTrigger>
          <TabsTrigger value="builds">
            {t("tab_builds", { count: builds.length })}
          </TabsTrigger>
          <TabsTrigger value="services">
            Servicios ({template.services?.length ?? 0})
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <Card className="mb-4">
            <CardHeader className="flex-row items-center justify-between gap-3 space-y-0">
              <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                Rama por ambiente & previews
              </CardTitle>
              <AutoPreviewToggle templateId={template.id} initial={template.autoPreviewPullRequests} />
            </CardHeader>
            <CardContent>
              <EnvironmentMappingEditor
                templateId={template.id}
                initial={template.environmentMapping ?? []}
                defaultBranch={template.branch}
              />
            </CardContent>
          </Card>
          {/* F12.3 — Branch-per-Instance mapping table + PR preview opt-in. */}
          {(template.environmentMapping?.length > 0 || template.autoPreviewPullRequests) ? (
            <Card className="mb-4">
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  Branch-per-Instance & Previews
                </CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      Default branch
                    </dt>
                    <dd className="mt-0.5 font-mono text-xs text-foreground">
                      {template.branch}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      Auto-preview pull requests
                    </dt>
                    <dd className="mt-0.5">
                      {template.autoPreviewPullRequests ? (
                        <Badge variant="success">enabled</Badge>
                      ) : (
                        <Badge variant="outline">disabled</Badge>
                      )}
                    </dd>
                  </div>
                  {template.environmentMapping?.length > 0 ? (
                    <div className="md:col-span-2">
                      <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                        Environment → Branch
                      </dt>
                      <dd className="mt-1">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Environment</TableHead>
                              <TableHead>Branch</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {template.environmentMapping.map((m) => (
                              <TableRow key={m.environment}>
                                <TableCell className="font-mono text-xs">{m.environment}</TableCell>
                                <TableCell className="font-mono text-xs">{m.branch}</TableCell>
                              </TableRow>
                            ))}
                          </TableBody>
                        </Table>
                      </dd>
                    </div>
                  ) : null}
                </dl>
              </CardContent>
            </Card>
          ) : null}
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                  {t("source_title")}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="flex flex-col gap-3 text-sm">
                  <Kv label={t("git_repo")} value={template.gitRepoUrl} mono />
                  <Kv label={t("branch")} value={template.branch} mono />
                  <Kv
                    label={t("base_directory")}
                    value={template.baseDirectory}
                    mono
                  />
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      {t("watch_paths")}
                    </dt>
                    <dd className="mt-1 flex flex-wrap gap-1">
                      {template.watchPaths.map((p) => (
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
                  {t("build_title")}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="flex flex-col gap-3 text-sm">
                  <Kv label={t("type_label")} value={template.buildType} />
                  {template.dockerfilePath ? (
                    <Kv
                      label={t("dockerfile")}
                      value={template.dockerfilePath}
                      mono
                    />
                  ) : null}
                  {template.composeFilePath ? (
                    <Kv
                      label={t("compose_file")}
                      value={template.composeFilePath}
                      mono
                    />
                  ) : null}
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      {t("build_args")}
                    </dt>
                    <dd className="mt-1">
                      {template.buildArgs.length === 0 ? (
                        <span className="text-xs text-muted-foreground">
                          {t("no_args")}
                        </span>
                      ) : (
                        <ul className="flex flex-col gap-1 font-mono text-[11px]">
                          {template.buildArgs.map((a) => (
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
              icon={<Rocket className="h-6 w-6" />}
              title={t("instances_empty_title")}
              description={t("instances_empty_description")}
              action={
                <Button asChild>
                  <Link href={`/templates/${template.id}/instances/new`}>
                    <Plus className="mr-2 h-4 w-4" />
                    {t("create_instance")}
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
                      {inst.customDomain ?? inst.autoHostname ?? tCommon("no_hostname")}
                    </p>
                    {inst.effectiveTrackedRef ? (
                      <p className="mt-1 flex items-center gap-1 truncate font-mono text-[10px] text-muted-foreground">
                        <GitBranch className="h-3 w-3" />
                        {inst.effectiveTrackedRef}
                      </p>
                    ) : null}
                  </Link>
                </li>
              ))}
            </ul>
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
                    <TableHead>{t("col_trigger")}</TableHead>
                    <TableHead>{t("col_created")}</TableHead>
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

        <TabsContent value="services" className="mt-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                Servicios (deploy nativo multi-contenedor)
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ServicesEditor templateId={template.id} initial={template.services ?? []} />
            </CardContent>
          </Card>
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
