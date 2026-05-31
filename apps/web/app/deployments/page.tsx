import Link from "next/link";

export const dynamic = "force-dynamic";

export default async function DeploymentsPage() {
  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-8">
        <header className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-3xl font-semibold">Deployments</h1>
            <p className="mt-1 text-sm text-zinc-500">
              Despliegues de instancias hacia las VMs, con estado y logs en vivo.
            </p>
          </div>
          <Link
            href="/projects"
            className="shrink-0 rounded-full border border-zinc-700 px-4 py-2 text-sm transition hover:bg-zinc-800"
          >
            Ir a proyectos
          </Link>
        </header>

        <section className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
          <h2 className="text-xl font-semibold text-zinc-100">
            Pipeline de Deployments — pendiente F9.3
          </h2>
          <p className="mt-2 text-sm text-slate-500">
            El módulo de deployments se entregará en la fase F9.3 del refactor
            greenfield.
          </p>
        </section>
      </div>
    </main>
  );
}
