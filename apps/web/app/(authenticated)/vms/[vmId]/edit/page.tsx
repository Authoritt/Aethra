import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { VmDto } from "@/lib/types";
import { EditVmForm } from "./EditVmForm";

export const dynamic = "force-dynamic";

export default async function EditVmPage({
  params,
}: {
  params: Promise<{ vmId: string }>;
}) {
  const { vmId } = await params;
  const res = await serverFetch<VmDto>(`/api/vms/${vmId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar la VM.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "VMs", href: "/vms" }, { label: res.name, href: `/vms/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.name}`}
        description={<span className="font-mono text-xs">{res.slug}</span>}
      />
      <EditVmForm vm={res} />
    </div>
  );
}
