import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { MonitorDetailDto } from "@/lib/types";
import { EditMonitorForm } from "./EditMonitorForm";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchMonitor(
  monitorId: string,
): Promise<MonitorDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/monitors/${monitorId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as MonitorDetailDto;
}

export default async function EditMonitorPage({
  params,
}: {
  params: Promise<{ monitorId: string }>;
}) {
  const { monitorId } = await params;
  const data = await fetchMonitor(monitorId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();
  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el monitor.
          </CardContent>
        </Card>
      </div>
    );
  }
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Monitores", href: "/monitors" },
          { label: data.name, href: `/monitors/${data.id}` },
          { label: "Editar" },
        ]}
        title="Editar monitor"
        description="Los cambios se aplican al próximo tick del worker."
      />
      <div className="max-w-2xl">
        <EditMonitorForm initial={data} />
      </div>
    </div>
  );
}
