import Link from "next/link";
import { redirect } from "next/navigation";
import { serverFetch } from "@/lib/server-fetch";
import type { ClientSummary, ProjectSummaryV2 } from "@/lib/types";

export const dynamic = "force-dynamic";

interface ProjectClients {
  project: ProjectSummaryV2;
  clients: ClientSummary[];
  error: boolean;
}

async function aggregateClients(): Promise<
  ProjectClients[] | "unauthorized" | "error"
> {
  // El contrato del backend expone clients solo anidados bajo project
  // (GET /api/projects/{id}/clients). Para un overview global hacemos
  // fan-out projects -> clients y los agrupamos por proyecto.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const lists = await Promise.all(
    projects.map((p) =>
      serverFetch<ClientSummary[]>(`/api/projects/${p.id}/clients`),
    ),
  );

  return projects.map((project, i) => {
    const result = lists[i];
    return {
      project,
      clients: Array.isArray(result) ? result : [],
      error: result === "error",
    };
  });
}

export default async function ClientsPage() {
  const data = await aggregateClients();
  if (data === "unauthorized") redirect("/login");

  const totalClients = Array.isArray(data)
    ? data.reduce((sum, g) => sum + g.clients.length, 0)
    : 0;
  const groupsWithClients = Array.isArray(data)
    ? data.filter((g) => g.clients.length > 0)
    : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Clients</h1>
            <p className="text-sm text-zinc-500">
              Tenants concretos de cada proyecto. Cada client recibe sus propias
              instancias de los templates del proyecto.
            </p>
          </div>
          <Link
            href="/projects"
            className="rounded-full border border-zinc-700 px-4 py-2 text-sm transition hover:bg-zinc-800"
          >
            Ir a proyectos
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && (
          <EmptyState
            title="Aun sin proyectos"
            body="Los clients viven dentro de un proyecto. Crea un proyecto primero y luego agrega su primer client."
          />
        )}

        {Array.isArray(data) && data.length > 0 && totalClients === 0 && (
          <EmptyState
            title="Aun sin clients"
            body="Ninguno de tus proyectos tiene clients todavia. Entra a un proyecto para crear el primero."
          />
        )}

        {groupsWithClients.length > 0 && (
          <div className="flex flex-col gap-8">
            {groupsWithClients.map((group) => (
              <section key={group.project.id} className="flex flex-col gap-3">
                <div className="flex items-center justify-between">
                  <h2 className="flex items-center gap-2 text-sm uppercase tracking-wider text-zinc-500">
                    {group.project.color && (
                      <span
                        className="size-3 shrink-0 rounded-full ring-1 ring-zinc-800"
                        style={{ backgroundColor: group.project.color }}
                        aria-hidden
                      />
                    )}
                    <Link
                      href={`/projects/${group.project.id}`}
                      className="hover:text-zinc-300"
                    >
                      {group.project.name}
                    </Link>
                    <span className="font-mono text-[11px] text-zinc-600">
                      {group.clients.length}
                    </span>
                  </h2>
                  <Link
                    href={`/projects/${group.project.id}/clients/new`}
                    className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800"
                  >
                    Crear client
                  </Link>
                </div>
                <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                  {group.clients.map((c) => (
                    <ClientCard key={c.id} client={c} />
                  ))}
                </ul>
              </section>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}

function ClientCard({ client }: { client: ClientSummary }) {
  return (
    <li>
      <Link
        href={`/clients/${client.id}`}
        className="block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
      >
        <h3 className="truncate text-lg font-semibold">{client.displayName}</h3>
        <p className="mt-1 font-mono text-xs text-zinc-500">{client.slug}</p>
        <div className="mt-3 flex flex-col gap-1 text-[11px] text-zinc-400">
          <span className="truncate">
            <span className="text-zinc-600">email: </span>
            {client.contactEmail ?? "—"}
          </span>
          {client.billingTag && (
            <span className="truncate font-mono">
              <span className="font-sans text-zinc-600">billing: </span>
              {client.billingTag}
            </span>
          )}
        </div>
      </Link>
    </li>
  );
}

function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">{title}</h2>
      <p className="mt-2 text-sm text-zinc-500">{body}</p>
      <Link
        href="/projects"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Ir a proyectos
      </Link>
    </div>
  );
}
