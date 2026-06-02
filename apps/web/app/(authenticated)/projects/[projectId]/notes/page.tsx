import { NotesList } from "@/app/(authenticated)/notes/_components/NotesList";
import { PinnedFactsPanel } from "@/app/(authenticated)/notes/_components/PinnedFactsPanel";
import { PageHeader } from "@/components/layout/page-header";

export const dynamic = "force-dynamic";

export default async function ProjectNotesPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Proyectos", href: "/projects" },
          { label: projectId, href: `/projects/${projectId}` },
          { label: "Notas" },
        ]}
        title="Notas del proyecto"
        description="Documentación markdown, imágenes adjuntas y pinned facts cifrados, todo asociado al proyecto."
      />

      <div className="flex flex-col gap-8">
        <PinnedFactsPanel scopeType="Project" scopeId={projectId} />

        <section className="flex flex-col gap-3">
          <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Notas
          </h2>
          <NotesList scopeType="Project" scopeId={projectId} />
        </section>
      </div>
    </div>
  );
}
