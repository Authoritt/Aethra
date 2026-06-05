import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { DnsRecordDto } from "@/lib/types";
import { EditDnsRecordForm } from "./EditDnsRecordForm";

export const dynamic = "force-dynamic";

export default async function EditDnsRecordPage({
  params,
}: {
  params: Promise<{ zoneId: string; recordId: string }>;
}) {
  const t = await getTranslations("pages.cloudflare_record_new");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { zoneId, recordId } = await params;

  // No hay GET de un solo record: traemos la lista de la zona y buscamos por id.
  const res = await serverFetch<DnsRecordDto[]>(`/api/cloudflare/zones/${zoneId}/records`);
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar el record.</div>;
  }
  const record = res.find((r) => r.id === recordId);
  if (!record) notFound();

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("cloudflare"), href: "/cloudflare" },
          { label: t("breadcrumb_zone"), href: `/cloudflare/${zoneId}` },
          { label: record.name },
        ]}
        title={`Editar ${record.name}`}
        description={<span className="font-mono text-xs">{record.type}</span>}
      />
      <EditDnsRecordForm zoneId={zoneId} record={record} />
    </div>
  );
}
