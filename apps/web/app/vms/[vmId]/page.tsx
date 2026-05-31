import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { VmDto, VmMetricPoint, VmStatus } from "@/lib/types";
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
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando la VM.
        </div>
      </main>
    );
  }

  const vm = data;
  const initialMetrics = await fetchLatestMetrics(vmId);

  const totalGb = vm.total_memory_bytes
    ? (vm.total_memory_bytes / 1024 / 1024 / 1024).toFixed(1)
    : null;

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/vms" className="hover:text-zinc-300">
            VMs
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{vm.name}</span>
        </nav>

        <header className="flex flex-col gap-3">
          <div className="flex items-center gap-3">
            <h1 className="text-3xl font-semibold">{vm.name}</h1>
            <StatusPill status={vm.status} />
          </div>
          <p className="font-mono text-xs text-zinc-500">{vm.slug}</p>
          {vm.description && (
            <p className="text-sm text-zinc-300">{vm.description}</p>
          )}
        </header>

        <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <Info label="IP pública" value={vm.public_ip ?? "—"} mono />
          <Info label="IP privada" value={vm.private_ip ?? "—"} mono />
          <Info label="Hostname" value={vm.hostname ?? "—"} mono />
          <Info
            label="Kernel"
            value={vm.kernel_version ?? "—"}
            mono
            truncate
          />
          <Info label="CPU" value={vm.cpu_model ?? "—"} truncate />
          <Info
            label="Cores"
            value={vm.cpu_cores ? `${vm.cpu_cores}` : "—"}
          />
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
    </main>
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
    <div className="rounded-xl border border-zinc-800 bg-zinc-900/40 p-3">
      <div className="text-[10px] uppercase tracking-wider text-zinc-500">
        {label}
      </div>
      <div
        className={`mt-1 text-sm text-zinc-200 ${mono ? "font-mono" : ""} ${
          truncate ? "truncate" : ""
        }`}
        title={truncate ? value : undefined}
      >
        {value}
      </div>
    </div>
  );
}

function StatusPill({ status }: { status: VmStatus }) {
  const styles: Record<string, string> = {
    Connected: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
    Pending: "border-zinc-700 bg-zinc-800/40 text-zinc-400",
    Disconnected: "border-rose-500/40 bg-rose-500/10 text-rose-300",
  };
  const dotStyles: Record<string, string> = {
    Connected: "bg-emerald-400",
    Pending: "bg-zinc-500",
    Disconnected: "bg-rose-400",
  };
  const klass = styles[status] ?? styles.Pending;
  const dot = dotStyles[status] ?? dotStyles.Pending;
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${klass}`}
    >
      <span className={`size-1.5 rounded-full ${dot}`} />
      {status}
    </span>
  );
}
