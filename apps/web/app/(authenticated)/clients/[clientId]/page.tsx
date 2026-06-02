import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { serverFetch } from "@/lib/server-fetch";
import type { ClientDetail, InstanceSummary } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ClientDetailPage({
  params,
}: {
  params: Promise<{ clientId: string }>;
}) {
  const { clientId } = await params;

  const [clientResult, instancesResult] = await Promise.all([
    serverFetch<ClientDetail>(`/api/clients/${clientId}`),
    serverFetch<InstanceSummary[]>(`/api/clients/${clientId}/instances`),
  ]);

  if (clientResult === "unauthorized") redirect("/login");
  if (clientResult === "notfound") notFound();
  if (clientResult === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el client.
        </div>
      </main>
    );
  }

  const client = clientResult;
  const instances = Array.isArray(instancesResult) ? instancesResult : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/projects" className="hover:text-zinc-300">
            Proyectos
          </Link>
          <span> / </span>
          <Link
            href={`/projects/${client.projectId}`}
            className="hover:text-zinc-300"
          >
            Proyecto
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{client.displayName}</span>
        </nav>

        <header className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="truncate text-3xl font-semibold">
              {client.displayName}
            </h1>
            <p className="mt-1 font-mono text-xs text-zinc-500">{client.slug}</p>
            {client.description && (
              <p className="mt-3 max-w-2xl text-sm text-zinc-300">
                {client.description}
              </p>
            )}
          </div>
          {/* Edit endpoint quedaria en F9.6; el boton vive aqui como placeholder
              hacia la futura ruta /clients/{id}/edit. */}
          <span
            className="shrink-0 rounded-full border border-zinc-800 bg-zinc-900/40 px-5 py-2 text-sm text-zinc-500"
            title="La edicion de clients llegara en una iteracion posterior."
            aria-disabled
          >
            Editar (pronto)
          </span>
        </header>

        <section className="grid grid-cols-1 gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 md:grid-cols-3">
          <Kv label="Contact email" value={client.contactEmail ?? "—"} />
          <Kv
            label="Billing tag"
            value={client.billingTag ?? "—"}
            mono={Boolean(client.billingTag)}
          />
          <Kv label="Instances" value={String(client.instanceCount)} />
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Instances{" "}
            <span className="ml-2 font-mono text-[11px] text-zinc-600">
              {instances.length}
            </span>
            {instancesResult === "error" && (
              <span className="ml-2 text-[11px] normal-case text-rose-400">
                (error al cargar)
              </span>
            )}
          </h2>
          {instances.length === 0 ? (
            <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
              Este client aun no tiene instancias. Creales una desde el detalle
              de un template.
            </p>
          ) : (
            <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
              {instances.map((inst) => (
                <li key={inst.id}>
                  <Link
                    href={`/instances/${inst.id}`}
                    className="block rounded-xl border border-zinc-800 bg-zinc-900/40 p-4 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <h3 className="truncate font-mono text-xs text-zinc-100">
                        {inst.slug}
                      </h3>
                      <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] uppercase tracking-wider text-zinc-400">
                        {inst.environment}
                      </span>
                    </div>
                    <p className="mt-2 font-mono text-[11px] text-zinc-500">
                      template {inst.templateId.slice(0, 8)}
                    </p>
                    <div className="mt-2">
                      <AutoHostnameInfo
                        autoHostname={inst.autoHostname}
                        customDomain={inst.customDomain}
                      />
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </main>
  );
}

function Kv({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wider text-zinc-500">{label}</dt>
      <dd
        className={`mt-0.5 break-all text-zinc-100 ${mono ? "font-mono text-xs" : "text-sm"}`}
      >
        {value}
      </dd>
    </div>
  );
}
