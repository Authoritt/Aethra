import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { FileCode, NotebookPen, Plus, Users } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ClientSummary,
  ProjectDetailV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ProjectDetailPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const t = await getTranslations("pages.projects_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { projectId } = await params;

  const [projectResult, templatesResult, clientsResult] = await Promise.all([
    serverFetch<ProjectDetailV2>(`/api/projects/${projectId}`),
    serverFetch<TemplateSummary[]>(`/api/projects/${projectId}/templates`),
    serverFetch<ClientSummary[]>(`/api/projects/${projectId}/clients`),
  ]);

  if (projectResult === "unauthorized") redirect("/login");
  if (projectResult === "notfound") notFound();

  if (projectResult === "error") {
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

  const project = projectResult;
  const templates = Array.isArray(templatesResult) ? templatesResult : [];
  const clients = Array.isArray(clientsResult) ? clientsResult : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("projects"), href: "/projects" },
          { label: project.name },
        ]}
        title={project.name}
        description={
          <>
            <span className="font-mono text-xs text-muted-foreground">
              {project.slug}
            </span>
            {project.description ? (
              <>
                <span className="mx-2 text-muted-foreground/50">·</span>
                {project.description}
              </>
            ) : null}
          </>
        }
        actions={
          <Button asChild variant="outline">
            <Link href={`/projects/${project.id}/notes`}>
              <NotebookPen className="mr-2 h-4 w-4" />
              {t("notes_action")}
            </Link>
          </Button>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">
          {t("templates_count", { count: project.templateCount })}
        </Badge>
        <Badge variant="outline">
          {t("clients_count", { count: project.clientCount })}
        </Badge>
        {project.color ? (
          <span
            className="inline-flex h-6 items-center gap-1.5 rounded-full border border-border bg-card px-2 text-xs text-muted-foreground"
            aria-label={t("color_aria", { color: project.color })}
          >
            <span
              className="size-3 rounded-full"
              style={{ backgroundColor: project.color }}
              aria-hidden
            />
            {project.color}
          </span>
        ) : null}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <section className="flex flex-col gap-3">
          <Card>
            <CardHeader className="flex-row items-center justify-between gap-2 space-y-0">
              <div>
                <CardTitle className="text-base">{t("templates_title")}</CardTitle>
                <CardDescription>
                  {t("templates_description")}{" "}
                  {templatesResult === "error" ? (
                    <span className="text-destructive">{t("templates_error")}</span>
                  ) : null}
                </CardDescription>
              </div>
              <Button asChild size="sm">
                <Link href={`/projects/${project.id}/templates/new`}>
                  <Plus className="mr-2 h-4 w-4" />
                  {t("new_button")}
                </Link>
              </Button>
            </CardHeader>
            <CardContent>
              {templates.length === 0 ? (
                <EmptyState
                  icon={<FileCode className="h-6 w-6" />}
                  title={t("templates_empty_title")}
                  description={t("templates_empty_description")}
                />
              ) : (
                <ul className="flex flex-col gap-2">
                  {templates.map((tpl) => (
                    <li key={tpl.id}>
                      <Link
                        href={`/templates/${tpl.id}`}
                        className="group block rounded-md border border-border bg-card p-4 transition-colors hover:border-primary/40"
                      >
                        <div className="flex items-start justify-between gap-2">
                          <h3 className="truncate text-sm font-semibold text-foreground">
                            {tpl.name}
                          </h3>
                          <Badge variant="outline">
                            {t("instance_count", { count: tpl.instanceCount ?? 0 })}
                          </Badge>
                        </div>
                        <p className="mt-1 font-mono text-[11px] text-muted-foreground">
                          {tpl.slug}
                        </p>
                        <p className="mt-2 truncate font-mono text-[11px] text-muted-foreground">
                          {tpl.gitRepoUrl}
                          <span className="text-muted-foreground/60"> @ </span>
                          {tpl.branch}
                        </p>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </section>

        <section className="flex flex-col gap-3">
          <Card>
            <CardHeader className="flex-row items-center justify-between gap-2 space-y-0">
              <div>
                <CardTitle className="text-base">{t("clients_title")}</CardTitle>
                <CardDescription>
                  {t("clients_description")}{" "}
                  {clientsResult === "error" ? (
                    <span className="text-destructive">{t("templates_error")}</span>
                  ) : null}
                </CardDescription>
              </div>
              <Button asChild size="sm">
                <Link href={`/projects/${project.id}/clients/new`}>
                  <Plus className="mr-2 h-4 w-4" />
                  {t("new_button")}
                </Link>
              </Button>
            </CardHeader>
            <CardContent>
              {clients.length === 0 ? (
                <EmptyState
                  icon={<Users className="h-6 w-6" />}
                  title={t("clients_empty_title")}
                  description={t("clients_empty_description")}
                />
              ) : (
                <ul className="flex flex-col gap-2">
                  {clients.map((c) => (
                    <li key={c.id}>
                      <Link
                        href={`/clients/${c.id}`}
                        className="group block rounded-md border border-border bg-card p-4 transition-colors hover:border-primary/40"
                      >
                        <div className="flex items-start justify-between gap-2">
                          <h3 className="truncate text-sm font-semibold text-foreground">
                            {c.displayName}
                          </h3>
                          <Badge variant="outline">
                            {t("instance_count", { count: c.instanceCount ?? 0 })}
                          </Badge>
                        </div>
                        <p className="mt-1 font-mono text-[11px] text-muted-foreground">
                          {c.slug}
                        </p>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </section>
      </div>
    </div>
  );
}
