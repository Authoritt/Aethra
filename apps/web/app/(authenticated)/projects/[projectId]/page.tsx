import Link from "next/link";
import { notFound, redirect } from "next/navigation";
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
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el proyecto.
        </div>
      </main>
    );
  }

  const project = projectResult;
  const templates = Array.isArray(templatesResult) ? templatesResult : [];
  const clients = Array.isArray(clientsResult) ? clientsResult : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
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
                  className="size-4 shrink-0 rounded-full ring-1 ring-zinc-800"
                  style={{ backgroundColor: project.color }}
                  aria-hidden
                />
              )}
              <h1 className="truncate text-3xl font-semibold">{project.name}</h1>
            </div>
            <p className="mt-1 font-mono text-xs text-zinc-500">{project.slug}</p>
            {project.description && (
              <p className="mt-3 max-w-2xl text-sm text-zinc-300">
                {project.description}
              </p>
            )}
            <div className="mt-4 flex gap-2 text-[11px] uppercase tracking-wider text-zinc-500">
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                templates {project.templateCount}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                clients {project.clientCount}
              </span>
            </div>
          </div>
          <Link
            href={`/projects/${project.id}/notes`}
            className="shrink-0 rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-200 transition hover:bg-zinc-800"
          >
            Notas y facts
          </Link>
        </header>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <section className="flex flex-col gap-3">
            <SectionHeader
              title="Templates"
              count={templates.length}
              ctaHref={`/projects/${project.id}/templates/new`}
              ctaLabel="Crear template"
              error={templatesResult === "error"}
            />
            {templates.length === 0 ? (
              <EmptyTile>
                Aun no hay templates en este proyecto. Crea el primero para
                definir el build de una imagen reutilizable.
              </EmptyTile>
            ) : (
              <ul className="flex flex-col gap-2">
                {templates.map((t) => (
                  <li key={t.id}>
                    <Link
                      href={`/templates/${t.id}`}
                      className="block rounded-xl border border-zinc-800 bg-zinc-900/40 p-4 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <h3 className="truncate text-sm font-semibold text-zinc-100">
                          {t.name}
                        </h3>
                        <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-400">
                          {t.instanceCount} inst
                        </span>
                      </div>
                      <p className="mt-1 font-mono text-[11px] text-zinc-500">
                        {t.slug}
                      </p>
                      <p className="mt-2 truncate font-mono text-[11px] text-zinc-400">
                        {t.gitRepoUrl}
                        <span className="text-zinc-600"> @ </span>
                        {t.branch}
                      </p>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="flex flex-col gap-3">
            <SectionHeader
              title="Clients"
              count={clients.length}
              ctaHref={`/projects/${project.id}/clients/new`}
              ctaLabel="Crear client"
              error={clientsResult === "error"}
            />
            {clients.length === 0 ? (
              <EmptyTile>
                Aun no hay clients. Los clients representan tenants concretos
                que tendran sus propias instancias.
              </EmptyTile>
            ) : (
              <ul className="flex flex-col gap-2">
                {clients.map((c) => (
                  <li key={c.id}>
                    <Link
                      href={`/clients/${c.id}`}
                      className="block rounded-xl border border-zinc-800 bg-zinc-900/40 p-4 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <h3 className="truncate text-sm font-semibold text-zinc-100">
                          {c.displayName}
                        </h3>
                        <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-400">
                          {c.instanceCount} inst
                        </span>
                      </div>
                      <p className="mt-1 font-mono text-[11px] text-zinc-500">
                        {c.slug}
                      </p>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </div>
    </main>
  );
}

function SectionHeader({
  title,
  count,
  ctaHref,
  ctaLabel,
  error,
}: {
  title: string;
  count: number;
  ctaHref: string;
  ctaLabel: string;
  error: boolean;
}) {
  return (
    <div className="flex items-center justify-between">
      <h2 className="text-sm uppercase tracking-wider text-zinc-500">
        {title}
        <span className="ml-2 font-mono text-[11px] text-zinc-600">{count}</span>
        {error && (
          <span className="ml-2 text-[11px] normal-case text-rose-400">
            (error al cargar)
          </span>
        )}
      </h2>
      <Link
        href={ctaHref}
        className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800"
      >
        {ctaLabel}
      </Link>
    </div>
  );
}

function EmptyTile({ children }: { children: React.ReactNode }) {
  return (
    <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
      {children}
    </p>
  );
}
