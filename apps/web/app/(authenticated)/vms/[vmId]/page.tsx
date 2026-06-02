import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { VmStatusPill } from "@/components/aethra/vm-status-pill";
import { API_URL } from "@/lib/api";
import type { VmDto, VmMetricPoint } from "@/lib/types";
import VmLiveDashboard from "./VmLiveDashboard";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchVm(
  vmId: string,
): Promise<VmDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/vms/${vmId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as VmDto;
}

async function fetchLatestMetrics(vmId: string): Promise<VmMetricPoint[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(
    `${API_URL}/api/metrics/vms/${vmId}/latest?limit=60`,
    {
      headers: { cookie: cookieHeader },
      cache: "no-store",
    },
  );
  if (!res.ok) return [];
  return (await res.json()) as VmMetricPoint[];
}

export default async function VmDetailPage({
  params,
}: {
  params: Promise<{ vmId: string }>;
}) {
  const { vmId } = await params;
  const data = await fetchVm(vmId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando la VM.
          </CardContent>
        </Card>
      </div>
    );
  }

  const vm = data;
  const initialMetrics = await fetchLatestMetrics(vmId);

  const totalGb = vm.total_memory_bytes
    ? (vm.total_memory_bytes / 1024 / 1024 / 1024).toFixed(1)
    : null;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "VMs", href: "/vms" }, { label: vm.name }]}
        title={vm.name}
        description={
          <>
            <span className="font-mono text-xs">{vm.slug}</span>
            {vm.description ? (
              <>
                <span className="mx-2 text-muted-foreground/50">·</span>
                {vm.description}
              </>
            ) : null}
          </>
        }
        actions={<VmStatusPill status={vm.status} />}
      />

      <section className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
        <Info label="IP pública" value={vm.public_ip ?? "—"} mono />
        <Info label="IP privada" value={vm.private_ip ?? "—"} mono />
        <Info label="Hostname" value={vm.hostname ?? "—"} mono />
        <Info label="Kernel" value={vm.kernel_version ?? "—"} mono truncate />
        <Info label="CPU" value={vm.cpu_model ?? "—"} truncate />
        <Info label="Cores" value={vm.cpu_cores ? `${vm.cpu_cores}` : "—"} />
        <Info label="RAM total" value={totalGb ? `${totalGb} GB` : "—"} />
        <Info label="Agente" value={vm.agent_version ?? "—"} mono />
      </section>

      <VmLiveDashboard
        vmId={vm.id}
        initialStatus={vm.status}
        initialMetrics={initialMetrics}
        totalMemoryBytes={vm.total_memory_bytes}
      />
    </div>
  );
}

function Info({
  label,
  value,
  mono,
  truncate,
}: {
  label: string;
  value: string;
  mono?: boolean;
  truncate?: boolean;
}) {
  return (
    <Card>
      <CardContent className="p-3">
        <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
          {label}
        </div>
        <div
          className={`mt-1 text-sm text-foreground ${mono ? "font-mono" : ""} ${
            truncate ? "truncate" : ""
          }`}
          title={truncate ? value : undefined}
        >
          {value}
        </div>
      </CardContent>
    </Card>
  );
}
