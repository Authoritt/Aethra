import Link from "next/link";
import { redirect } from "next/navigation";
import { ChevronRight, FileCode, Plus } from "lucide-react";
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
import type { ProjectSummaryV2, TemplateSummary } from "@/lib/types";

export const dynamic = "force-dynamic";

interface ProjectTemplates {
  project: ProjectSummaryV2;
  templates: TemplateSummary[];
  error: boolean;
}

async function aggregateTemplates(): Promise<
  ProjectTemplates[] | "unauthorized" | "error"
> {
  // El contrato del backend expone templates solo anidados bajo project
  // (GET /api/projects/{id}/templates). Para un overview global hacemos
  // fan-out projects -> templates y los agrupamos por proyecto.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const lists = await Promise.all(
    projects.map((p) =>
      serverFetch<TemplateSummary[]>(`/api/projects/${p.id}/templates`),
    ),
  );

  return projects.map((project, i) => {
    const result = lists[i];
    return {
      project,
      templates: Array.isArray(result) ? result : [],
      error: result === "error",
    };
  });
}

export default async function TemplatesPage() {
  const data = await aggregateTemplates();
  if (data === "unauthorized") redirect("/login");

  const groups = Array.isArray(data) ? data : [];
  const errored = data === "error";
  const totalTemplates = groups.reduce((sum, g) => sum + g.templates.length, 0);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10 space-y-8">
      <PageHeader
        title="Plantillas"
        description="Plantillas reutilizables de build (Dockerfile, Compose o Nixpacks) agrupadas por proyecto. Cada una genera builds e instancias."
        actions={
          <Button asChild variant="outline">
            <Link href="/projects">
              Ir a proyectos
              <ChevronRight className="ml-1 h-4 w-4" />
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado. Verificá que la API esté corriendo.
          </CardContent>
        </Card>
      ) : groups.length === 0 ? (
        <EmptyState
          icon={<FileCode className="h-6 w-6" />}
          title="Aún sin proyectos"
          description="Los templates viven dentro de un proyecto. Creá un proyecto primero y luego definí su primer template."
          action={
            <Button asChild>
              <Link href="/projects/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear proyecto
              </Link>
            </Button>
          }
        />
      ) : totalTemplates === 0 ? (
        <EmptyState
          icon={<FileCode className="h-6 w-6" />}
          title="Aún sin plantillas"
          description="Aún no hay plantillas registradas. Entra a un proyecto para crear la primera."
          action={
            <Button asChild variant="outline">
              <Link href="/projects">
                Ver proyectos
                <ChevronRight className="ml-1 h-4 w-4" />
              </Link>
            </Button>
          }
        />
      ) : (
        groups
          .filter((g) => g.templates.length > 0)
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
                    {group.templates.length}
                  </Badge>
                </div>
                <div className="flex items-center gap-2">
                  <Button asChild variant="ghost" size="sm">
                    <Link href={`/projects/${group.project.id}/templates/new`}>
                      <Plus className="mr-1 h-4 w-4" />
                      Crear template
                    </Link>
                  </Button>
                  <Button asChild variant="outline" size="sm">
                    <Link href={`/projects/${group.project.id}`}>
                      Ver proyecto
                      <ChevronRight className="ml-1 h-4 w-4" />
                    </Link>
                  </Button>
                </div>
              </header>

              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {group.templates.map((t) => (
                  <Card
                    key={t.id}
                    className="transition-colors hover:border-primary/40"
                  >
                    <CardHeader className="pb-3">
                      <div className="flex items-start justify-between gap-2">
                        <CardTitle className="truncate text-base">
                          {t.name}
                        </CardTitle>
                        <Badge
                          variant="outline"
                          className="shrink-0 font-mono text-[10px] uppercase"
                        >
                          {t.buildType}
                        </Badge>
                      </div>
                      <CardDescription className="font-mono text-xs">
                        {t.slug}
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="pb-3">
                      <p
                        className="truncate font-mono text-[11px] text-muted-foreground"
                        title={`${t.gitRepoUrl} @ ${t.branch}`}
                      >
                        {t.gitRepoUrl}
                        <span className="opacity-60"> @ </span>
                        {t.branch}
                      </p>
                    </CardContent>
                    <CardFooter className="pt-0">
                      <Button asChild variant="ghost" size="sm">
                        <Link href={`/templates/${t.id}`}>
                          Detalles
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
