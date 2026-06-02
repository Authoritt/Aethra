import Link from "next/link";
import { redirect } from "next/navigation";
import { serverFetch } from "@/lib/server-fetch";
import type { ProjectSummaryV2 } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ProjectsPage() {
  const data = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (data === "unauthorized") redirect("/login");

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Proyectos</h1>
            <p className="text-sm text-zinc-500">
              Agrupaciones logicas que contienen templates y clients del modelo multi-tenant.
            </p>
          </div>
          <Link
            href="/projects/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Nuevo proyecto
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && <EmptyState />}

        {Array.isArray(data) && data.length > 0 && (
          <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
            {data.map((p) => (
              <ProjectCard key={p.id} project={p} />
            ))}
          </ul>
        )}
      </div>
    </main>
  );
}

function ProjectCard({ project }: { project: ProjectSummaryV2 }) {
  return (
    <li>
      <Link
        href={`/projects/${project.id}`}
        className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
      >
        <div className="flex items-start justify-between gap-2">
          <h3 className="truncate text-lg font-semibold">{project.name}</h3>
          {project.color && (
            <span
              className="mt-1 size-3 shrink-0 rounded-full ring-1 ring-zinc-800"
              style={{ backgroundColor: project.color }}
              aria-hidden
            />
          )}
        </div>
        <p className="mt-1 font-mono text-xs text-zinc-500">{project.slug}</p>
        {project.icon && (
          <p className="mt-2 inline-flex items-center gap-1 rounded-md border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-400">
            icon: {project.icon}
          </p>
        )}
      </Link>
    </li>
  );
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aun sin proyectos</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea tu primer proyecto. Despues podras agregar templates y clients.
      </p>
      <Link
        href="/projects/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Crear proyecto
      </Link>
    </div>
  );
}
