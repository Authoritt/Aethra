import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ManagedServiceSummaryDto } from "@/lib/types";
import { ServiceStatusPill } from "./ServiceStatusPill";

export const dynamic = "force-dynamic";

async function fetchServices(): Promise<
  ManagedServiceSummaryDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/services`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ManagedServiceSummaryDto[];
}

export default async function ServicesPage() {
  const data = await fetchServices();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Servicios compartidos</h1>
            <p className="text-sm text-zinc-500">
              Postgres, Redis, RabbitMQ y otros backends provisionados desde
              plantillas. Consumidos vía bindings desde applications.
            </p>
          </div>
          <Link
            href="/services/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Crear servicio
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
                  <th className="px-4 py-3">Slug</th>
                  <th className="px-4 py-3">Tipo</th>
                  <th className="px-4 py-3">Versión</th>
                  <th className="px-4 py-3">VM target</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3 text-right">Bindings</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((s) => (
                  <tr
                    key={s.id}
                    className="transition hover:bg-zinc-900/60"
                  >
                    <td className="px-4 py-3">
                      <Link
                        href={`/services/${s.id}`}
                        className="flex flex-col gap-0.5"
                      >
                        <span className="font-mono text-zinc-100">
                          {s.slug}
                        </span>
                        <span className="text-xs text-zinc-500">
                          {s.name}
                        </span>
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <TypeChip type={s.type} />
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-300">
                      {s.version}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-400">
                      {s.target_vm_id}
                    </td>
                    <td className="px-4 py-3">
                      <ServiceStatusPill status={s.status} />
                    </td>
                    <td className="px-4 py-3 text-right">
                      <span className="inline-flex min-w-[2rem] justify-center rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-xs text-zinc-300">
                        {s.bindings_count}
                      </span>
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

function TypeChip({ type }: { type: string }) {
  return (
    <span className="inline-flex items-center rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-300">
      {type}
    </span>
  );
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">
        Aún sin servicios
      </h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea un Postgres, Redis o RabbitMQ desde plantilla para que tus
        applications puedan consumirlo vía bindings.
      </p>
      <Link
        href="/services/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Crear servicio
      </Link>
    </div>
  );
}
