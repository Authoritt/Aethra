import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Boxes, ChevronRight, ExternalLink } from "lucide-react";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type {
  InstanceSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedInstance extends InstanceSummary {
  templateName: string;
  templateSlug: string;
  projectName: string;
  projectId: string;
  projectSlug: string;
  projectColor: string | null;
}

interface ProjectInstances {
  project: {
    id: string;
    name: string;
    slug: string;
    color: string | null;
  };
  instances: AggregatedInstance[];
}

async function aggregateInstances(): Promise<
  ProjectInstances[] | "unauthorized" | "error"
> {
  // El backend solo expone instances anidadas bajo template
  // (GET /api/templates/{id}/instances). Para el overview global hacemos
  // fan-out projects -> templates -> instances y agrupamos por proyecto,
  // anotando cada instance con su template y proyecto para dar contexto.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const perProject = await Promise.all(
    projects.map(async (p) => {
      const templates = await serverFetch<TemplateSummary[]>(
        `/api/projects/${p.id}/templates`,
      );
      if (!Array.isArray(templates) || templates.length === 0) {
        return { project: p, instances: [] as AggregatedInstance[] };
      }

      const instanceLists = await Promise.all(
        templates.map(async (template) => {
          const instances = await serverFetch<InstanceSummary[]>(
            `/api/templates/${template.id}/instances`,
          );
          if (!Array.isArray(instances)) return [] as AggregatedInstance[];
          return instances.map<AggregatedInstance>((inst) => ({
            ...inst,
            templateName: template.name,
            templateSlug: template.slug,
            projectName: p.name,
            projectId: p.id,
            projectSlug: p.slug,
            projectColor: p.color || null,
          }));
        }),
      );

      const merged = instanceLists.flat();
      merged.sort(
        (a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      );
      return { project: p, instances: merged };
    }),
  );

  return perProject.map(({ project, instances }) => ({
    project: {
      id: project.id,
      name: project.name,
      slug: project.slug,
      color: project.color || null,
    },
    instances,
  }));
}

export default async function InstancesPage() {
  const t = await getTranslations("pages.instances_list");
  const tCommon = await getTranslations("common");
  const data = await aggregateInstances();
  if (data === "unauthorized") redirect("/login");

  const groups = Array.isArray(data) ? data : [];
  const errored = data === "error";
  const totalInstances = groups.reduce(
    (sum, g) => sum + g.instances.length,
    0,
  );

  return (
    <div className="px-6 py-8 md:px-10 md:py-10 space-y-8">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Button asChild variant="outline">
            <Link href="/projects">
              {tCommon("go_to_projects")}
              <ChevronRight className="ml-1 h-4 w-4" />
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error")}
          </CardContent>
        </Card>
      ) : totalInstances === 0 ? (
        <EmptyState
          icon={<Boxes className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild variant="outline">
              <Link href="/templates">
                {t("see_templates")}
                <ChevronRight className="ml-1 h-4 w-4" />
              </Link>
            </Button>
          }
        />
      ) : (
        groups
          .filter((g) => g.instances.length > 0)
          .map((group) => (
            <section key={group.project.id} className="space-y-3">
              <header className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-3">
                  {group.project.color ? (
                    <span
                      className="size-3 shrink-0 rounded-full ring-1 ring-border"
                      style={{ backgroundColor: group.project.color }}
                      aria-hidden
                    />
                  ) : null}
                  <h2 className="text-lg font-semibold tracking-tight text-foreground">
                    {group.project.name}
                  </h2>
                  <span className="font-mono text-xs text-muted-foreground">
                    {group.project.slug}
                  </span>
                  <Badge variant="outline" className="font-mono text-[10px]">
                    {group.instances.length}
                  </Badge>
                </div>
                <Button asChild variant="outline" size="sm">
                  <Link href={`/projects/${group.project.id}`}>
                    {tCommon("go_to_project")}
                    <ChevronRight className="ml-1 h-4 w-4" />
                  </Link>
                </Button>
              </header>

              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {group.instances.map((inst) => (
                  <Card
                    key={inst.id}
                    className="transition-colors hover:border-primary/40"
                  >
                    <CardHeader className="pb-3">
                      <div className="flex items-start justify-between gap-2">
                        <CardTitle className="truncate font-mono text-sm">
                          {inst.slug}
                        </CardTitle>
                        <Badge
                          variant="outline"
                          className="shrink-0 font-mono text-[10px] uppercase"
                        >
                          {inst.environment}
                        </Badge>
                      </div>
                      <CardDescription className="truncate font-mono text-xs">
                        {inst.containerName}
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="space-y-2 pb-3 text-xs">
                      <div className="flex items-baseline gap-2">
                        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
                          {t("label_template")}
                        </span>
                        <Link
                          href={`/templates/${inst.templateId}`}
                          className="truncate font-mono text-foreground hover:text-primary"
                          title={inst.templateName}
                        >
                          {inst.templateSlug}
                        </Link>
                      </div>
                      <div className="flex items-baseline gap-2">
                        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
                          {t("label_client")}
                        </span>
                        <Link
                          href={`/clients/${inst.clientId}`}
                          className="truncate font-mono text-foreground hover:text-primary"
                        >
                          {inst.clientSlug || inst.clientId.slice(0, 8)}
                        </Link>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 pt-1">
                        <AutoHostnameInfo
                          autoHostname={inst.autoHostname}
                          customDomain={inst.customDomain}
                        />
                        {inst.customDomain ? (
                          <span
                            className="inline-flex items-center gap-1 truncate font-mono text-[11px] text-muted-foreground"
                            title={inst.customDomain}
                          >
                            <ExternalLink className="h-3 w-3" aria-hidden />
                            {inst.customDomain}
                          </span>
                        ) : null}
                      </div>
                      <div className="flex items-center gap-2 pt-1">
                        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
                          {t("label_autodeploy")}
                        </span>
                        {inst.autoDeployOnNewBuild ? (
                          <Badge
                            variant="success"
                            className="font-mono text-[10px] uppercase"
                          >
                            {t("on")}
                          </Badge>
                        ) : (
                          <Badge
                            variant="outline"
                            className="font-mono text-[10px] uppercase"
                          >
                            {t("off")}
                          </Badge>
                        )}
                      </div>
                    </CardContent>
                    <CardFooter className="pt-0">
                      <Button asChild variant="ghost" size="sm">
                        <Link href={`/instances/${inst.id}`}>
                          {t("details")}
                          <ChevronRight className="ml-1 h-4 w-4" />
                        </Link>
                      </Button>
                    </CardFooter>
                  </Card>
                ))}
              </div>
            </section>
          ))
      )}
    </div>
  );
}
