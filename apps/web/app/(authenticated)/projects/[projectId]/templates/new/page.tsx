import { notFound, redirect } from "next/navigation";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
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
  const project = await serverFetch<ProjectDetailV2>(
    `/api/projects/${projectId}`,
  );
  if (project === "unauthorized") redirect("/login");
  if (project === "notfound") notFound();

  if (project === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el proyecto.
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Proyectos", href: "/projects" },
          { label: project.name, href: `/projects/${project.id}` },
          { label: "Nuevo template" },
        ]}
        title="Nuevo template"
        description="Definí el repo Git y la estrategia de build. Al crear el template recibirás un webhook secret que solo se mostrará una vez."
      />
      <div className="max-w-3xl">
        <NewTemplateForm projectId={project.id} />
      </div>
    </div>
  );
}
