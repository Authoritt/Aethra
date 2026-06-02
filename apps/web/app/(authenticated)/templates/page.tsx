import Link from "next/link";
import { redirect } from "next/navigation";
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

  const totalTemplates = Array.isArray(data)
    ? data.reduce((sum, g) => sum + g.templates.length, 0)
    : 0;
  const groupsWithTemplates = Array.isArray(data)
    ? data.filter((g) => g.templates.length > 0)
    : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Templates</h1>
            <p className="text-sm text-zinc-500">
              Plantillas reutilizables de build (Dockerfile, Compose o Nixpacks)
              agrupadas por proyecto. Cada una genera builds e instancias.
            </p>
          </div>
          <Link
            href="/projects"
            className="rounded-full border border-zinc-700 px-4 py-2 text-sm transition hover:bg-zinc-800"
          >
            Ir a proyectos
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && (
          <EmptyState
            title="Aun sin proyectos"
            body="Los templates viven dentro de un proyecto. Crea un proyecto primero y luego define su primer template."
          />
        )}

        {Array.isArray(data) && data.length > 0 && totalTemplates === 0 && (
          <EmptyState
            title="Aun sin templates"
            body="Ninguno de tus proyectos tiene templates todavia. Entra a un proyecto para crear el primero."
          />
        )}

        {groupsWithTemplates.length > 0 && (
          <div className="flex flex-col gap-8">
            {groupsWithTemplates.map((group) => (
              <section key={group.project.id} className="flex flex-col gap-3">
                <div className="flex items-center justify-between">
                  <h2 className="flex items-center gap-2 text-sm uppercase tracking-wider text-zinc-500">
                    {group.project.color && (
                      <span
                        className="size-3 shrink-0 rounded-full ring-1 ring-zinc-800"
                        style={{ backgroundColor: group.project.color }}
                        aria-hidden
                      />
                    )}
                    <Link
                      href={`/projects/${group.project.id}`}
                      className="hover:text-zinc-300"
                    >
                      {group.project.name}
                    </Link>
                    <span className="font-mono text-[11px] text-zinc-600">
                      {group.templates.length}
                    </span>
                  </h2>
                  <Link
                    href={`/projects/${group.project.id}/templates/new`}
                    className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800"
                  >
                    Crear template
                  </Link>
                </div>
                <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                  {group.templates.map((t) => (
                    <TemplateCard key={t.id} template={t} />
                  ))}
                </ul>
              </section>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}

function TemplateCard({ template }: { template: TemplateSummary }) {
  return (
    <li>
      <Link
        href={`/templates/${template.id}`}
        className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
      >
        <div className="flex items-start justify-between gap-2">
          <h3 className="truncate text-lg font-semibold">{template.name}</h3>
          <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-400">
            {template.buildType}
          </span>
        </div>
        <p className="mt-1 font-mono text-xs text-zinc-500">{template.slug}</p>
        <p className="mt-3 truncate font-mono text-[11px] text-zinc-400">
          {template.gitRepoUrl}
          <span className="text-zinc-600"> @ </span>
          {template.branch}
        </p>
      </Link>
    </li>
  );
}

function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">{title}</h2>
      <p className="mt-2 text-sm text-zinc-500">{body}</p>
      <Link
        href="/projects"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Ir a proyectos
      </Link>
    </div>
  );
}
