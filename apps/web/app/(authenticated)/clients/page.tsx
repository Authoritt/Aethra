import Link from "next/link";
import { redirect } from "next/navigation";
import { ChevronRight, Plus, Users } from "lucide-react";
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
import type { ClientSummary, ProjectSummaryV2 } from "@/lib/types";

export const dynamic = "force-dynamic";

interface ProjectClients {
  project: ProjectSummaryV2;
  clients: ClientSummary[];
  error: boolean;
}

async function aggregateClients(): Promise<
  ProjectClients[] | "unauthorized" | "error"
> {
  // El contrato del backend expone clients solo anidados bajo project
  // (GET /api/projects/{id}/clients). Para un overview global hacemos
  // fan-out projects -> clients y los agrupamos por proyecto.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const lists = await Promise.all(
    projects.map((p) =>
      serverFetch<ClientSummary[]>(`/api/projects/${p.id}/clients`),
    ),
  );

  return projects.map((project, i) => {
    const result = lists[i];
    return {
      project,
      clients: Array.isArray(result) ? result : [],
      error: result === "error",
    };
  });
}

export default async function ClientsPage() {
  const data = await aggregateClients();
  if (data === "unauthorized") redirect("/login");

  const groups = Array.isArray(data) ? data : [];
  const errored = data === "error";
  const totalClients = groups.reduce((sum, g) => sum + g.clients.length, 0);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10 space-y-8">
      <PageHeader
        title="Clients"
        description="Tenants concretos de cada proyecto. Cada client recibe sus propias instancias de los templates del proyecto."
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
          icon={<Users className="h-6 w-6" />}
          title="Aún sin proyectos"
          description="Los clients viven dentro de un proyecto. Creá un proyecto primero y luego agregá su primer client."
          action={
            <Button asChild>
              <Link href="/projects/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear proyecto
              </Link>
            </Button>
          }
        />
      ) : totalClients === 0 ? (
        <EmptyState
          icon={<Users className="h-6 w-6" />}
          title="Aún sin clients"
          description="Aún no hay clientes registrados. Entra a un proyecto para crear el primero."
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
          .filter((g) => g.clients.length > 0)
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
                    {group.clients.length}
                  </Badge>
                </div>
                <div className="flex items-center gap-2">
                  <Button asChild variant="ghost" size="sm">
                    <Link href={`/projects/${group.project.id}/clients/new`}>
                      <Plus className="mr-1 h-4 w-4" />
                      Crear client
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
                {group.clients.map((c) => (
                  <Card
                    key={c.id}
                    className="transition-colors hover:border-primary/40"
                  >
                    <CardHeader className="pb-3">
                      <CardTitle className="truncate text-base">
                        {c.displayName}
                      </CardTitle>
                      <CardDescription className="font-mono text-xs">
                        {c.slug}
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="space-y-1.5 pb-3 text-xs">
                      <div className="flex items-baseline gap-2">
                        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
                          email
                        </span>
                        <span className="truncate font-mono text-foreground">
                          {c.contactEmail ?? "—"}
                        </span>
                      </div>
                      {c.billingTag ? (
                        <div className="flex items-baseline gap-2">
                          <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
                            billing
                          </span>
                          <span className="truncate font-mono text-foreground">
                            {c.billingTag}
                          </span>
                        </div>
                      ) : null}
                    </CardContent>
                    <CardFooter className="pt-0">
                      <Button asChild variant="ghost" size="sm">
                        <Link href={`/clients/${c.id}`}>
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
