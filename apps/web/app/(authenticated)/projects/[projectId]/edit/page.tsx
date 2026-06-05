import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ProjectDetailV2 } from "@/lib/types";
import { EditProjectForm } from "./EditProjectForm";

export const dynamic = "force-dynamic";

export default async function EditProjectPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  const res = await serverFetch<ProjectDetailV2>(`/api/projects/${projectId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar el proyecto.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Proyectos", href: "/projects" }, { label: res.name, href: `/projects/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.name}`}
        description={<span className="font-mono text-xs">{res.slug}</span>}
      />
      <EditProjectForm project={res} />
    </div>
  );
}
