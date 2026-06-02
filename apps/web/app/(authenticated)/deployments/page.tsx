import Link from "next/link";
import { redirect } from "next/navigation";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ClientSummary,
  DeploymentSummary,
  InstanceSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedDeployment extends DeploymentSummary {
  instanceSlug: string;
  templateName: string;
  templateId: string;
  clientDisplayName: string;
  environment: string;
}

async function aggregate(
  searchParams: { instanceId?: string; status?: string },
): Promise<AggregatedDeployment[] | "unauthorized" | "error"> {
  // Si vino filtrado por instance, atajamos al endpoint directo.
  if (searchParams.instanceId) {
    const direct = await serverFetch<DeploymentSummary[]>(
      `/api/deployments/instances/${searchParams.instanceId}`,
    );
    if (direct === "unauthorized") return "unauthorized";
    if (direct === "error") return "error";
    if (!Array.isArray(direct)) return [];
    return direct.slice(0, 50).map((d) => ({
      ...d,
      instanceSlug: searchParams.instanceId!.slice(0, 8),
      templateName: "—",
      templateId: "",
      clientDisplayName: "—",
      environment: "—",
    }));
  }

  // Overview global: fan-out projects -> templates -> instances -> deployments.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const [templateLists, clientLists] = await Promise.all([
    Promise.all(
      projects.map((p) =>
        serverFetch<TemplateSummary[]>(`/api/projects/${p.id}/templates`),
      ),
    ),
    Promise.all(
      projects.map((p) =>
        serverFetch<ClientSummary[]>(`/api/projects/${p.id}/clients`),
      ),
    ),
  ]);
  const templates = templateLists
    .filter((t): t is TemplateSummary[] => Array.isArray(t))
    .flat();
  const clientsById = new Map<string, ClientSummary>();
  for (const list of clientLists) {
    if (Array.isArray(list)) {
      for (const c of list) clientsById.set(c.id, c);
    }
  }

  const templatesById = new Map(templates.map((t) => [t.id, t]));

  // Recorremos instances por template para conocer su contexto.
  const instanceLists = await Promise.all(
    templates.map((t) =>
      serverFetch<InstanceSummary[]>(`/api/templates/${t.id}/instances`),
    ),
  );
  const instances: InstanceSummary[] = instanceLists
    .filter((i): i is InstanceSummary[] => Array.isArray(i))
    .flat();

  if (instances.length === 0) return [];

  const deployLists = await Promise.all(
    instances.map(async (inst) => {
      const list = await serverFetch<DeploymentSummary[]>(
        `/api/deployments/instances/${inst.id}`,
      );
      if (!Array.isArray(list)) return [] as AggregatedDeployment[];
      const tpl = templatesById.get(inst.templateId);
      const client = clientsById.get(inst.clientId);
      return list.map((d) => ({
        ...d,
        instanceSlug: inst.slug,
        templateName: tpl?.name ?? "—",
        templateId: inst.templateId,
        clientDisplayName: client?.displayName ?? "—",
        environment: inst.environment,
      }));
    }),
  );
  let merged = deployLists.flat();
  merged.sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
  if (searchParams.status) {
    const wanted = searchParams.status.toLowerCase();
    merged = merged.filter((d) => d.status.toLowerCase() === wanted);
  }
  return merged.slice(0, 50);
}

export default async function DeploymentsPage({
  searchParams,
}: {
  searchParams: Promise<{ instanceId?: string; status?: string }>;
}) {
  const sp = await searchParams;
  const data = await aggregate(sp);
  if (data === "unauthorized") redirect("/login");

  const filterStatus = sp.status ?? "";
  const filterInstance = sp.instanceId ?? "";

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-3xl font-semibold">Deployments</h1>
            <p className="text-sm text-zinc-500">
              Ultimos 50 deployments agregados de todas las instancias.
            </p>
          </div>
          <Link
            href="/projects"
            className="rounded-full border border-zinc-700 px-4 py-2 text-sm transition hover:bg-zinc-800"
          >
            Ir a proyectos
          </Link>
        </header>

        <form
          className="flex flex-wrap items-end gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4"
          method="get"
        >
          <label className="flex flex-col text-xs text-zinc-300">
            Instance ID
            <input
              type="text"
              name="instanceId"
              defaultValue={filterInstance}
              placeholder="uuid"
              className="mt-1 w-56 rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 font-mono text-[11px] text-zinc-100 outline-none focus:border-emerald-500"
            />
          </label>
          <label className="flex flex-col text-xs text-zinc-300">
            Status
            <select
              name="status"
              defaultValue={filterStatus}
              className="mt-1 rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-xs text-zinc-100 outline-none focus:border-emerald-500"
            >
              <option value="">(todos)</option>
              <option value="Pending">Pending</option>
              <option value="Running">Running</option>
              <option value="Completed">Completed</option>
              <option value="Failed">Failed</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </label>
          <button
            type="submit"
            className="rounded-full border border-zinc-700 px-4 py-2 text-xs text-zinc-200 transition hover:bg-zinc-800"
          >
            Filtrar
          </button>
          {(filterStatus || filterInstance) && (
            <Link
              href="/deployments"
              className="text-xs text-zinc-400 underline-offset-2 hover:underline"
            >
              Limpiar
            </Link>
          )}
        </form>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && (
          <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
            <h2 className="text-xl font-semibold">Sin deployments</h2>
            <p className="mt-2 text-sm text-zinc-500">
              {filterInstance || filterStatus
                ? "No hay deployments que cumplan el filtro."
                : "Aun no se ha desplegado ninguna instancia."}
            </p>
          </div>
        )}

        {Array.isArray(data) && data.length > 0 && (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Instance</th>
                  <th className="px-4 py-3">Template</th>
                  <th className="px-4 py-3">Client</th>
                  <th className="px-4 py-3">Env</th>
                  <th className="px-4 py-3">Trigger</th>
                  <th className="px-4 py-3">Creado</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((d) => (
                  <tr key={d.id} className="hover:bg-zinc-900/60">
                    <td className="px-4 py-3">
                      <Link href={`/deployments/${d.id}`} className="inline-block">
                        <StatusPill status={d.status} />
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        href={`/instances/${d.instanceId}`}
                        className="font-mono text-xs text-zinc-200 hover:text-emerald-300"
                      >
                        {d.instanceSlug}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-sm text-zinc-300">
                      {d.templateId ? (
                        <Link
                          href={`/templates/${d.templateId}`}
                          className="hover:text-emerald-300"
                        >
                          {d.templateName}
                        </Link>
                      ) : (
                        d.templateName
                      )}
                    </td>
                    <td className="px-4 py-3 text-sm text-zinc-300">
                      {d.clientDisplayName}
                    </td>
                    <td className="px-4 py-3 text-xs uppercase tracking-wider text-zinc-400">
                      {d.environment}
                    </td>
                    <td className="px-4 py-3 text-xs text-zinc-400">
                      {d.trigger}
                    </td>
                    <td className="px-4 py-3 text-xs text-zinc-400">
                      {formatDate(d.createdAt)}
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

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
