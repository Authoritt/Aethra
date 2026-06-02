import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { VmDto, VmStatus } from "@/lib/types";

export const dynamic = "force-dynamic";

async function fetchVms(): Promise<VmDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/vms/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as VmDto[];
}

export default async function VmsPage() {
  const data = await fetchVms();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">VMs</h1>
            <p className="text-sm text-zinc-500">
              Hosts Oracle gestionados por Aethra. Las métricas se reciben via
              satélite.
            </p>
          </div>
          <Link
            href="/vms/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Registrar VM
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API esté corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && <EmptyState />}

        {Array.isArray(data) && data.length > 0 && (
          <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
            {data.map((vm) => (
              <VmCard key={vm.id} vm={vm} />
            ))}
          </ul>
        )}
      </div>
    </main>
  );
}

function VmCard({ vm }: { vm: VmDto }) {
  const totalGb = vm.total_memory_bytes
    ? (vm.total_memory_bytes / 1024 / 1024 / 1024).toFixed(1)
    : null;

  return (
    <li>
      <Link
        href={`/vms/${vm.id}`}
        className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
      >
        <div className="flex items-start justify-between gap-3">
          <h3 className="truncate text-lg font-semibold">{vm.name}</h3>
          <StatusPill status={vm.status} />
        </div>
        <p className="mt-1 font-mono text-xs text-zinc-500">{vm.slug}</p>
        {vm.description && (
          <p className="mt-3 line-clamp-2 text-sm text-zinc-300">
            {vm.description}
          </p>
        )}

        <dl className="mt-4 grid grid-cols-2 gap-3 text-xs">
          <div>
            <dt className="uppercase tracking-wider text-zinc-500">IP pública</dt>
            <dd className="mt-0.5 font-mono text-zinc-300">
              {vm.public_ip ?? "—"}
            </dd>
          </div>
          <div>
            <dt className="uppercase tracking-wider text-zinc-500">CPU</dt>
            <dd className="mt-0.5 text-zinc-300">
              {vm.cpu_cores ? `${vm.cpu_cores} cores` : "—"}
            </dd>
          </div>
          <div>
            <dt className="uppercase tracking-wider text-zinc-500">RAM</dt>
            <dd className="mt-0.5 text-zinc-300">
              {totalGb ? `${totalGb} GB` : "—"}
            </dd>
          </div>
          <div>
            <dt className="uppercase tracking-wider text-zinc-500">Agente</dt>
            <dd className="mt-0.5 font-mono text-zinc-300">
              {vm.agent_version ?? "—"}
            </dd>
          </div>
        </dl>
      </Link>
    </li>
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

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aún sin VMs</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Registra tu primera VM para conectar un satélite y ver métricas en
        tiempo real.
      </p>
      <Link
        href="/vms/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Registrar VM
      </Link>
    </div>
  );
}
