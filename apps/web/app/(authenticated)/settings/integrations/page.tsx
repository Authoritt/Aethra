import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { IntegrationCredentialDto } from "@/lib/types";
import { DeleteIntegrationButton } from "./DeleteIntegrationButton";

export const dynamic = "force-dynamic";

async function fetchCredentials(): Promise<
  IntegrationCredentialDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/integrations/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as IntegrationCredentialDto[];
}

export default async function IntegrationsPage() {
  const data = await fetchCredentials();
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
          <span className="text-zinc-300">Integraciones</span>
        </nav>

        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Integraciones</h1>
            <p className="text-sm text-zinc-500">
              Credenciales externas cifradas con DataProtection. Otros modulos
              las resuelven por <span className="font-mono">name</span>{" "}
              (formato <span className="font-mono">namespace:slug</span>).
            </p>
          </div>
          <Link
            href="/settings/integrations/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Nueva credencial
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
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Tipo</th>
                  <th className="px-4 py-3">Display</th>
                  <th className="px-4 py-3">Creada</th>
                  <th className="px-4 py-3">Ultima rotacion</th>
                  <th className="px-4 py-3">Ultimo uso</th>
                  <th className="px-4 py-3 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((c) => (
                  <tr key={c.id} className="transition hover:bg-zinc-900/60">
                    <td className="px-4 py-3 align-top">
                      <span className="rounded border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-[11px] text-zinc-200">
                        {c.name}
                      </span>
                      <div className="mt-0.5 font-mono text-[10px] text-zinc-500">
                        {c.id}
                      </div>
                    </td>
                    <td className="px-4 py-3 align-top">
                      <TypeBadge type={c.type} />
                    </td>
                    <td className="px-4 py-3 align-top">
                      <div className="text-zinc-100">{c.displayName}</div>
                      {c.description && (
                        <div className="mt-0.5 text-xs text-zinc-500">
                          {c.description}
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3 align-top text-xs text-zinc-400">
                      {formatDate(c.createdAt)}
                    </td>
                    <td className="px-4 py-3 align-top text-xs text-zinc-400">
                      {formatRelative(c.rotatedAt)}
                    </td>
                    <td className="px-4 py-3 align-top text-xs text-zinc-400">
                      {formatRelative(c.lastUsedAt)}
                    </td>
                    <td className="px-4 py-3 text-right align-top">
                      <div className="inline-flex items-center gap-2">
                        <Link
                          href={`/settings/integrations/${encodeURIComponent(
                            c.id,
                          )}/rotate`}
                          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
                        >
                          Rotar
                        </Link>
                        <DeleteIntegrationButton id={c.id} name={c.name} />
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
            Como resolver una credencial
          </h3>
          <p className="mt-2">
            Desde C# inyecta{" "}
            <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">
              IIntegrationCredentialResolver
            </code>{" "}
            y llama{" "}
            <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">
              GetSecretAsync(&quot;cloudflare:default&quot;, ct)
            </code>
            . El valor en claro nunca se persiste; solo se descifra en memoria
            al consumirlo.
          </p>
        </aside>
      </div>
    </main>
  );
}

function TypeBadge({ type }: { type: IntegrationCredentialDto["type"] }) {
  const color: Record<IntegrationCredentialDto["type"], string> = {
    Cloudflare: "border-orange-500/40 bg-orange-500/10 text-orange-300",
    GitHubPat: "border-zinc-500/40 bg-zinc-500/10 text-zinc-300",
    Smtp: "border-sky-500/40 bg-sky-500/10 text-sky-300",
    Registry: "border-violet-500/40 bg-violet-500/10 text-violet-300",
    GenericApiKey: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  };
  return (
    <span
      className={`inline-flex items-center rounded-full border px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider ${color[type]}`}
    >
      {type}
    </span>
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

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">
        Aun sin credenciales
      </h2>
      <p className="mt-2 text-sm text-zinc-500">
        Centraliza aqui las credenciales que comparten varios modulos
        (Cloudflare, GitHub, registries...). El valor en claro solo se ve una
        vez, en el momento de crearla.
      </p>
      <Link
        href="/settings/integrations/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Nueva credencial
      </Link>
    </div>
  );
}
