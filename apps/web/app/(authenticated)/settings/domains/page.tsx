import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { BaseDomainDto } from "@/lib/types";
import { ActivateBaseDomainButton } from "./ActivateBaseDomainButton";
import { MarkWildcardButton } from "./MarkWildcardButton";
import { DeleteBaseDomainButton } from "./DeleteBaseDomainButton";

export const dynamic = "force-dynamic";

async function fetchDomains(): Promise<
  BaseDomainDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/domains/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as BaseDomainDto[];
}

export default async function BaseDomainsPage() {
  const data = await fetchDomains();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <Link href="/settings" className="hover:text-zinc-300">
            Settings
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Base domains</span>
        </nav>

        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Base domains</h1>
            <p className="text-sm text-zinc-500">
              FQDN bajo el cual Aethra construye los hostnames de los recursos
              administrados. Solo uno puede estar activo a la vez.
            </p>
          </div>
          <Link
            href="/settings/domains/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Nuevo base domain
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && <EmptyState />}

        {Array.isArray(data) && data.length > 0 && (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Hostname</th>
                  <th className="px-4 py-3">Cloudflare zone</th>
                  <th className="px-4 py-3">Wildcard DNS</th>
                  <th className="px-4 py-3">Estado</th>
                  <th className="px-4 py-3">Creado</th>
                  <th className="px-4 py-3 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((d) => (
                  <tr key={d.id} className="transition hover:bg-zinc-900/60">
                    <td className="px-4 py-3 align-top">
                      <span className="font-mono text-xs text-zinc-100">
                        {d.hostname}
                      </span>
                      <div className="mt-0.5 font-mono text-[10px] text-zinc-500">
                        {d.id}
                      </div>
                    </td>
                    <td className="px-4 py-3 align-top text-xs">
                      {d.cloudflareZoneId ? (
                        <Link
                          href={`/cloudflare/${encodeURIComponent(
                            d.cloudflareZoneId,
                          )}`}
                          className="rounded border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-zinc-200 hover:border-emerald-500/40"
                        >
                          {d.cloudflareZoneId}
                        </Link>
                      ) : (
                        <span className="text-zinc-500">no enlazada</span>
                      )}
                    </td>
                    <td className="px-4 py-3 align-top">
                      {d.wildcardConfigured ? (
                        <span className="inline-flex items-center rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-emerald-300">
                          confirmado
                        </span>
                      ) : (
                        <MarkWildcardButton id={d.id} />
                      )}
                    </td>
                    <td className="px-4 py-3 align-top">
                      {d.isActive ? (
                        <span className="inline-flex items-center rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-emerald-300">
                          activo
                        </span>
                      ) : (
                        <span className="inline-flex items-center rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-zinc-400">
                          inactivo
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 align-top text-xs text-zinc-400">
                      {formatDate(d.createdAt)}
                    </td>
                    <td className="px-4 py-3 text-right align-top">
                      <div className="inline-flex items-center gap-2">
                        {!d.isActive && (
                          <ActivateBaseDomainButton
                            id={d.id}
                            hostname={d.hostname}
                          />
                        )}
                        <DeleteBaseDomainButton
                          id={d.id}
                          hostname={d.hostname}
                          isActive={d.isActive}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <aside className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
          <h3 className="text-xs uppercase tracking-wider text-zinc-500">
            Como funciona el wildcard
          </h3>
          <p className="mt-2">
            Crea en tu DNS un registro <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">*.tu-base-domain</code>
            {" "}apuntando a la IP publica del Edge VM. Cuando lo verifiques,
            marca <em>Wildcard configurado</em> para que el modulo Proxy use
            ese hostname como wildcard SAN al pedir certificados.
          </p>
        </aside>
      </div>
    </main>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "-";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">
        Aun sin base domains
      </h2>
      <p className="mt-2 text-sm text-zinc-500">
        Registra el FQDN bajo el cual Aethra creara hostnames (ej.{" "}
        <span className="font-mono">aethra.tu-empresa.com</span>). Despues
        marca el wildcard como configurado cuando crees el registro DNS.
      </p>
      <Link
        href="/settings/domains/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Nuevo base domain
      </Link>
    </div>
  );
}
