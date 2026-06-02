import Link from "next/link";
import { redirect } from "next/navigation";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { serverFetch } from "@/lib/server-fetch";
import type {
  InstanceSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedInstance extends InstanceSummary {
  templateName: string;
  templateSlug: string;
  projectName: string;
}

async function aggregateInstances(): Promise<
  AggregatedInstance[] | "unauthorized" | "error"
> {
  // El backend solo expone instances anidadas bajo template
  // (GET /api/templates/{id}/instances). Para el overview global hacemos
  // fan-out projects -> templates -> instances y mergeamos, anotando
  // cada instance con su template y proyecto para dar contexto en la tabla.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const templateLists = await Promise.all(
    projects.map(async (p) => {
      const templates = await serverFetch<TemplateSummary[]>(
        `/api/projects/${p.id}/templates`,
      );
      if (!Array.isArray(templates)) return [];
      return templates.map((t) => ({ template: t, projectName: p.name }));
    }),
  );
  const templates = templateLists.flat();
  if (templates.length === 0) return [];

  const instanceLists = await Promise.all(
    templates.map(async ({ template, projectName }) => {
      const instances = await serverFetch<InstanceSummary[]>(
        `/api/templates/${template.id}/instances`,
      );
      if (!Array.isArray(instances)) return [] as AggregatedInstance[];
      return instances.map((inst) => ({
        ...inst,
        templateName: template.name,
        templateSlug: template.slug,
        projectName,
      }));
    }),
  );

  const merged = instanceLists.flat();
  merged.sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
  return merged;
}

export default async function InstancesPage() {
  const data = await aggregateInstances();
  if (data === "unauthorized") redirect("/login");

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Instances</h1>
            <p className="text-sm text-zinc-500">
              Despliegues concretos: template x client x environment. Cada
              instancia corre en una VM con su propio hostname.
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
          <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
            <h2 className="text-xl font-semibold">Sin instances aun</h2>
            <p className="mt-2 text-sm text-zinc-500">
              Las instancias se crean desde el detalle de un template
              (template x client x environment). Entra a un template para crear
              la primera.
            </p>
            <Link
              href="/templates"
              className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              Ver templates
            </Link>
          </div>
        )}

        {Array.isArray(data) && data.length > 0 && (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Instance</th>
                  <th className="px-4 py-3">Template</th>
                  <th className="px-4 py-3">Client</th>
                  <th className="px-4 py-3">Env</th>
                  <th className="px-4 py-3">Hostname</th>
                  <th className="px-4 py-3 text-right">Auto-deploy</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((inst) => (
                  <tr key={inst.id} className="hover:bg-zinc-900/60">
                    <td className="px-4 py-3">
                      <Link
                        href={`/instances/${inst.id}`}
                        className="font-mono text-xs text-zinc-100 hover:text-emerald-300"
                      >
                        {inst.slug}
                      </Link>
                      <div className="font-mono text-[10px] text-zinc-500">
                        {inst.projectName}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        href={`/templates/${inst.templateId}`}
                        className="text-xs text-zinc-200 hover:text-emerald-300"
                      >
                        {inst.templateName}
                      </Link>
                      <div className="font-mono text-[10px] text-zinc-500">
                        {inst.templateSlug}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        href={`/clients/${inst.clientId}`}
                        className="font-mono text-xs text-zinc-300 hover:text-emerald-300"
                      >
                        {inst.clientSlug || inst.clientId.slice(0, 8)}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <span className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] uppercase tracking-wider text-zinc-400">
                        {inst.environment}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <AutoHostnameInfo
                        autoHostname={inst.autoHostname}
                        customDomain={inst.customDomain}
                      />
                    </td>
                    <td className="px-4 py-3 text-right">
                      {inst.autoDeployOnNewBuild ? (
                        <span className="rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium text-emerald-300">
                          on
                        </span>
                      ) : (
                        <span className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] font-medium text-zinc-400">
                          off
                        </span>
                      )}
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
