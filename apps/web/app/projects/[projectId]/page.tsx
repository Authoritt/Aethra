import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ProjectSummary } from "@/lib/types";

export const dynamic = "force-dynamic";

async function fetchProject(
  projectId: string,
): Promise<ProjectSummary | "unauthorized" | "notfound" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/projects/${projectId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as ProjectSummary;
}

export default async function ProjectDetailPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  const data = await fetchProject(projectId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el proyecto.
        </div>
      </main>
    );
  }

  const project = data;
  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/projects" className="hover:text-zinc-300">
            Proyectos
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{project.name}</span>
        </nav>

        <header className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="flex items-center gap-3">
              {project.color && (
                <span
                  className="size-4 rounded-full"
                  style={{ backgroundColor: project.color }}
                />
              )}
              <h1 className="text-3xl font-semibold">{project.name}</h1>
            </div>
            <p className="mt-1 font-mono text-xs text-zinc-500">{project.slug}</p>
            {project.description && (
              <p className="mt-3 text-sm text-zinc-300">{project.description}</p>
            )}
          </div>
          <Link
            href={`/projects/${project.id}/notes`}
            className="shrink-0 rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-200 transition hover:bg-zinc-800"
          >
            Notas y facts
          </Link>
        </header>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Templates
          </h2>
          <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-slate-500">
            Pendiente F9.5
          </p>
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Clients
          </h2>
          <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-slate-500">
            Pendiente F9.5
          </p>
        </section>
      </div>
    </main>
  );
}
