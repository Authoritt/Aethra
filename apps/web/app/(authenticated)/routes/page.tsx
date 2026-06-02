import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { CertStatus, RouteDto } from "@/lib/types";
import { DeleteRouteButton } from "./delete-route-button";

export const dynamic = "force-dynamic";

async function fetchRoutes(): Promise<RouteDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/proxy/routes`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as RouteDto[];
}

export default async function RoutesPage() {
  const data = await fetchRoutes();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Rutas</h1>
            <p className="text-sm text-zinc-500">
              Reverse proxy YARP con terminación TLS por hostname.
            </p>
          </div>
          <Link
            href="/routes/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Nueva ruta
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API esté corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && <EmptyState />}

        {Array.isArray(data) && data.length > 0 && (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Hostname</th>
                  <th className="px-4 py-3">Backend</th>
                  <th className="px-4 py-3">TLS</th>
                  <th className="px-4 py-3">Expira</th>
                  <th className="px-4 py-3 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((r) => (
                  <tr key={r.id} className="hover:bg-zinc-900/60">
                    <td className="px-4 py-3 font-medium text-zinc-100">
                      {r.hostname}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-300">
                      {r.backend_url}
                    </td>
                    <td className="px-4 py-3">
                      <CertPill
                        status={r.tls_enabled ? r.cert_status : "none"}
                        tlsEnabled={r.tls_enabled}
                      />
                    </td>
                    <td className="px-4 py-3 text-zinc-400">
                      {formatExpires(r.cert_expires_at)}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <DeleteRouteButton id={r.id} hostname={r.hostname} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </main>
  );
}

function CertPill({
  status,
  tlsEnabled,
}: {
  status: CertStatus;
  tlsEnabled: boolean;
}) {
  if (!tlsEnabled) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full border border-zinc-700 bg-zinc-800/40 px-2.5 py-0.5 text-[11px] font-medium text-zinc-400">
        <span className="size-1.5 rounded-full bg-zinc-500" />
        sin TLS
      </span>
    );
  }
  const styles: Record<CertStatus, string> = {
    issued: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
    pending: "border-amber-500/40 bg-amber-500/10 text-amber-300",
    renewing: "border-amber-500/40 bg-amber-500/10 text-amber-300",
    failed: "border-rose-500/40 bg-rose-500/10 text-rose-300",
    none: "border-zinc-700 bg-zinc-800/40 text-zinc-400",
  };
  const dots: Record<CertStatus, string> = {
    issued: "bg-emerald-400",
    pending: "bg-amber-400",
    renewing: "bg-amber-400",
    failed: "bg-rose-400",
    none: "bg-zinc-500",
  };
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${styles[status]}`}
    >
      <span className={`size-1.5 rounded-full ${dots[status]}`} />
      {status}
    </span>
  );
}

function formatExpires(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aún sin rutas</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea tu primera ruta para exponer un backend con TLS automático.
      </p>
      <Link
        href="/routes/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Crear ruta
      </Link>
    </div>
  );
}
