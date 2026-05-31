import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ApiKeySummary } from "@/lib/types";
import { ApiKeyStatusPill, deriveStatus } from "./ApiKeyStatusPill";
import { RevokeKeyButton } from "./RevokeKeyButton";

export const dynamic = "force-dynamic";

async function fetchKeys(): Promise<
  ApiKeySummary[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/api-keys`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ApiKeySummary[];
}

export default async function ApiKeysPage() {
  const data = await fetchKeys();
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
          <span className="text-zinc-300">API keys</span>
        </nav>

        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">API keys</h1>
            <p className="text-sm text-zinc-500">
              Tokens portadores para integrar herramientas externas y agentes
              con la API de Aethra. El secret se muestra una unica vez al
              crearlas.
            </p>
          </div>
          <Link
            href="/settings/api-keys/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Crear API key
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
                  <th className="px-4 py-3">Nombre</th>
                  <th className="px-4 py-3">Prefix</th>
                  <th className="px-4 py-3">Scopes</th>
                  <th className="px-4 py-3">Creada</th>
                  <th className="px-4 py-3">Ultimo uso</th>
                  <th className="px-4 py-3">Expira</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((key) => {
                  const status = deriveStatus(key);
                  return (
                    <tr key={key.id} className="transition hover:bg-zinc-900/60">
                      <td className="px-4 py-3 align-top">
                        <div className="font-medium text-zinc-100">
                          {key.name}
                        </div>
                        <div className="mt-0.5 font-mono text-[10px] text-zinc-500">
                          {key.id}
                        </div>
                      </td>
                      <td className="px-4 py-3 align-top">
                        <span className="rounded border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-[11px] text-zinc-300">
                          {key.key_prefix}
                          <span className="text-zinc-600">...</span>
                        </span>
                      </td>
                      <td className="px-4 py-3 align-top">
                        <ScopesPills scopes={key.scopes} />
                      </td>
                      <td className="px-4 py-3 align-top text-xs text-zinc-400">
                        {formatDate(key.created_at)}
                      </td>
                      <td className="px-4 py-3 align-top text-xs text-zinc-400">
                        {formatRelative(key.last_used_at)}
                      </td>
                      <td className="px-4 py-3 align-top text-xs text-zinc-400">
                        {formatExpires(key.expires_at)}
                      </td>
                      <td className="px-4 py-3 align-top">
                        <ApiKeyStatusPill status={status} />
                      </td>
                      <td className="px-4 py-3 text-right align-top">
                        <RevokeKeyButton
                          id={key.id}
                          name={key.name}
                          alreadyRevoked={status === "revoked"}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        <aside className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
          <h3 className="text-xs uppercase tracking-wider text-zinc-500">
            Como usar una API key
          </h3>
          <p className="mt-2">
            Envia el secret en el header{" "}
            <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">
              Authorization: Bearer aethra_...
            </code>{" "}
            o como{" "}
            <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">
              X-Api-Key
            </code>{" "}
            segun la integracion.
          </p>
        </aside>
      </div>
    </main>
  );
}

function ScopesPills({ scopes }: { scopes: string[] }) {
  if (scopes.length === 0) {
    return <span className="text-xs text-zinc-500">(ninguno)</span>;
  }
  if (scopes.includes("*")) {
    return (
      <span className="inline-flex items-center rounded-full border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 font-mono text-[10px] text-amber-300">
        admin (*)
      </span>
    );
  }
  const max = 4;
  const visible = scopes.slice(0, max);
  const overflow = scopes.length - max;
  return (
    <div className="flex flex-wrap gap-1">
      {visible.map((s) => (
        <span
          key={s}
          className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-300"
        >
          {s}
        </span>
      ))}
      {overflow > 0 && (
        <span className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-400">
          +{overflow}
        </span>
      )}
    </div>
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

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "-";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "hace unos seg.";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  return `hace ${days} d`;
}

function formatExpires(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  return formatDate(iso);
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aun sin API keys</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Crea tu primera API key para que tus integraciones, scripts y agentes
        IA puedan llamar a la API de Aethra. El secret solo se muestra una vez,
        en el momento de crearla.
      </p>
      <Link
        href="/settings/api-keys/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Crear API key
      </Link>
    </div>
  );
}
