import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ClientDetail } from "@/lib/types";
import { EditClientForm } from "./EditClientForm";

export const dynamic = "force-dynamic";

export default async function EditClientPage({
  params,
}: {
  params: Promise<{ clientId: string }>;
}) {
  const { clientId } = await params;
  const res = await serverFetch<ClientDetail>(`/api/clients/${clientId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar el cliente.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Clientes", href: "/clients" }, { label: res.displayName, href: `/clients/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.displayName}`}
        description={<span className="font-mono text-xs">{res.slug}</span>}
      />
      <EditClientForm client={res} />
    </div>
  );
}
