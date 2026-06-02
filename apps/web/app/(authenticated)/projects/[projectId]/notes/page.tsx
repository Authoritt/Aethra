import Link from "next/link";
import { NotesList } from "@/app/(authenticated)/notes/_components/NotesList";
import { PinnedFactsPanel } from "@/app/(authenticated)/notes/_components/PinnedFactsPanel";

export const dynamic = "force-dynamic";

/**
 * Página de notas y pinned facts del proyecto. Toda la mecánica vive en componentes
 * cliente — esta página solo arma la jerarquía visual y pasa el scope.
 */
export default async function ProjectNotesPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/projects" className="hover:text-zinc-300">
            Proyectos
          </Link>
          <span> / </span>
          <Link
            href={`/projects/${projectId}`}
            className="hover:text-zinc-300"
          >
            {projectId}
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Notas</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Notas del proyecto</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Documentación markdown, imágenes adjuntas y pinned facts cifrados,
            todo asociado al proyecto.
          </p>
        </header>

        <PinnedFactsPanel scopeType="Project" scopeId={projectId} />

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Notas
          </h2>
          <NotesList scopeType="Project" scopeId={projectId} />
        </section>
      </div>
    </main>
  );
}
