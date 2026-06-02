import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { serverFetch } from "@/lib/server-fetch";
import type { ProjectDetailV2 } from "@/lib/types";
import { NewTemplateForm } from "./NewTemplateForm";

export const dynamic = "force-dynamic";

export default async function NewTemplatePage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  const project = await serverFetch<ProjectDetailV2>(`/api/projects/${projectId}`);
  if (project === "unauthorized") redirect("/login");
  if (project === "notfound") notFound();

  if (project === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el proyecto.
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link href="/projects" className="hover:text-zinc-300">
            Proyectos
          </Link>
          <span> / </span>
          <Link
            href={`/projects/${project.id}`}
            className="hover:text-zinc-300"
          >
            {project.name}
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nuevo template</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Nuevo template</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Define el repo Git y la estrategia de build. Al crear el template
            recibiras un webhook secret que solo se mostrara una vez.
          </p>
        </header>

        <NewTemplateForm projectId={project.id} />
      </div>
    </main>
  );
}
