import { notFound, redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { RouteDto } from "@/lib/types";
import { EditRouteForm } from "./EditRouteForm";

export const dynamic = "force-dynamic";

export default async function EditRoutePage({
  params,
}: {
  params: Promise<{ routeId: string }>;
}) {
  const { routeId } = await params;
  const res = await serverFetch<RouteDto>(`/api/proxy/routes/${routeId}`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar la ruta.</div>;
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Rutas", href: "/routes" }, { label: res.hostname, href: `/routes/${res.id}` }, { label: "Editar" }]}
        title={`Editar ${res.hostname}`}
        description={<span className="font-mono text-xs">{res.pathPrefix || "/"}</span>}
      />
      <EditRouteForm route={res} />
    </div>
  );
}
