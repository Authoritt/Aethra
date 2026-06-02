import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { EnvironmentDefinitionDto } from "@/lib/types";
import { EnvironmentsManager } from "./EnvironmentsManager";

export const dynamic = "force-dynamic";

async function fetchEnvironments(): Promise<
  EnvironmentDefinitionDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/environments/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as EnvironmentDefinitionDto[];
}

export default async function EnvironmentsPage() {
  const data = await fetchEnvironments();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-4xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <Link href="/settings" className="hover:text-zinc-300">
            Settings
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Ambientes</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Ambientes</h1>
          <p className="text-sm text-zinc-500">
            Catalogo de ambientes validos (production, staging, preview...).
            El orden refleja la progresion natural y se respeta en la UI de
            otros modulos.
          </p>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && (
          <EnvironmentsManager initial={data} />
        )}

        <aside className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
          <h3 className="text-xs uppercase tracking-wider text-zinc-500">
            Como se consume
          </h3>
          <p className="mt-2">
            Otros modulos (Projects, Deployments) inyectan{" "}
            <code className="rounded border border-zinc-800 bg-zinc-950 px-1.5 py-0.5 font-mono text-[11px] text-zinc-200">
              IEnvironmentCatalog
            </code>{" "}
            y validan slugs contra esta lista. Asi se evita que cada modulo
            duplique el enum de ambientes.
          </p>
        </aside>
      </div>
    </main>
  );
}
