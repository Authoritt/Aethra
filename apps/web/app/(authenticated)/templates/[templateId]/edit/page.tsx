import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { TemplateDetail } from "@/lib/types";
import { EditTemplateForm } from "./EditTemplateForm";

export const dynamic = "force-dynamic";

export default async function EditTemplatePage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const { templateId } = await params;
  const res = await serverFetch<TemplateDetail>(`/api/templates/${templateId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar la plantilla.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Plantillas", href: "/templates" }, { label: res.name, href: `/templates/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.name}`}
        description={<span className="font-mono text-xs">{res.slug}</span>}
      />
      <EditTemplateForm template={res} />
    </div>
  );
}
