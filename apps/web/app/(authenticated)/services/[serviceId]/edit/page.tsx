import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ManagedServiceDetailDto } from "@/lib/types";
import { EditServiceForm } from "./EditServiceForm";

export const dynamic = "force-dynamic";

export default async function EditServicePage({
  params,
}: {
  params: Promise<{ serviceId: string }>;
}) {
  const { serviceId } = await params;
  const res = await serverFetch<ManagedServiceDetailDto>(`/api/services/${serviceId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar el servicio.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Servicios", href: "/services" }, { label: res.slug, href: `/services/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.name}`}
        description={<span className="font-mono text-xs">{res.slug}</span>}
      />
      <EditServiceForm service={res} />
    </div>
  );
}
