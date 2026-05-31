import Link from "next/link";
import { API_URL } from "@/lib/api";

// La página depende del estado de la API en tiempo de request, no en build time.
// Sin esto, `next build` intenta prerenderizar y se cuelga esperando al servidor.
export const dynamic = "force-dynamic";

interface HealthResponse {
  status: string;
  service: string;
  time: string;
  version: string;
}

async function fetchHealth(): Promise<HealthResponse | { error: string }> {
  try {
    const res = await fetch(`${API_URL}/health`, { cache: "no-store" });
    if (!res.ok) return { error: `${res.status}` };
    return await res.json();
  } catch (e) {
    return { error: e instanceof Error ? e.message : "unreachable" };
  }
}

export default async function Home() {
  const health = await fetchHealth();
  const ok = "status" in health && health.status === "ok";

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 bg-zinc-950 px-6 py-24 text-zinc-100">
      <div className="flex flex-col items-center gap-2">
        <h1 className="text-5xl font-semibold tracking-tight">Aethra</h1>
        <p className="text-zinc-400">
          Plataforma unificada de despliegue, monitoreo y operación.
        </p>
      </div>

      <section className="flex w-full max-w-md flex-col gap-4 rounded-2xl border border-zinc-800 bg-zinc-900/50 p-6">
        <div className="flex items-center justify-between">
          <span className="text-sm uppercase tracking-wider text-zinc-500">
            API
          </span>
          <span
            className={`flex items-center gap-2 text-sm font-medium ${
              ok ? "text-emerald-400" : "text-rose-400"
            }`}
          >
            <span
              className={`size-2 rounded-full ${
                ok ? "bg-emerald-400" : "bg-rose-400"
              }`}
            />
            {ok ? "operativa" : "no alcanzable"}
          </span>
        </div>
        <pre className="overflow-x-auto rounded-lg bg-black/40 p-3 text-xs text-zinc-300">
          {JSON.stringify(health, null, 2)}
        </pre>
        <p className="text-xs text-zinc-500">
          URL: <code>{API_URL}</code>
        </p>
      </section>

      <div className="flex gap-3">
        <Link
          href="/login"
          className="rounded-full bg-emerald-500 px-6 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
        >
          Iniciar sesión
        </Link>
        <a
          href={`${API_URL}/openapi/v1.json`}
          target="_blank"
          rel="noopener noreferrer"
          className="rounded-full border border-zinc-700 px-6 py-2 text-sm font-medium text-zinc-300 transition hover:bg-zinc-800"
        >
          OpenAPI
        </a>
      </div>

      <footer className="mt-12 text-xs text-zinc-600">
        F0 — andamiaje y esqueleto modular
      </footer>
    </main>
  );
}
