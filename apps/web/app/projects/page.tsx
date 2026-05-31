import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ProjectSummary } from "@/lib/types";

export const dynamic = "force-dynamic";

async function fetchProjects(): Promise<ProjectSummary[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/projects/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ProjectSummary[];
}

export default async function ProjectsPage() {
  const data = await fetchProjects();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Proyectos</h1>
            <p className="text-sm text-zinc-500">
              Agrupaciones lógicas que contendrán templates y clients (F9.5).
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
            No se pudo cargar el listado. Verifica que la API esté corriendo.
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

function ProjectCard({ project }: { project: ProjectSummary }) {
  return (
    <li>
      <Link
        href={`/projects/${project.id}`}
        className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
      >
        <div className="flex items-start justify-between">
          <h3 className="text-lg font-semibold">{project.name}</h3>
          {project.color && (
            <span
              className="size-3 rounded-full"
              style={{ backgroundColor: project.color }}
            />
          )}
        </div>
        <p className="mt-1 font-mono text-xs text-zinc-500">{project.slug}</p>
        {project.description && (
          <p className="mt-3 text-sm text-zinc-300">{project.description}</p>
        )}
      </Link>
    </li>
  );
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aún sin proyectos</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea tu primer proyecto. Después podrás añadir templates y clients (F9.5).
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
