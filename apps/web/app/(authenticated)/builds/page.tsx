import Link from "next/link";
import { redirect } from "next/navigation";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedBuild extends BuildSummary {
  templateName: string;
  templateSlug: string;
}

async function aggregateRecentBuilds(): Promise<
  AggregatedBuild[] | "unauthorized" | "error"
> {
  // El contrato de A10 expone builds solo por template. Para un overview global
  // hacemos fan-out: projects -> templates -> builds, y mergeamos por fecha
  // desc. Cap a 50 para no abusar.
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const templateLists = await Promise.all(
    projects.map((p) =>
      serverFetch<TemplateSummary[]>(`/api/projects/${p.id}/templates`),
    ),
  );
  const templates: TemplateSummary[] = templateLists
    .filter((t): t is TemplateSummary[] => Array.isArray(t))
    .flat();

  if (templates.length === 0) return [];

  const buildLists = await Promise.all(
    templates.map(async (t) => {
      const builds = await serverFetch<BuildSummary[]>(
        `/api/builds/templates/${t.id}`,
      );
      if (!Array.isArray(builds)) return [] as AggregatedBuild[];
      return builds.map((b) => ({
        ...b,
        templateName: t.name,
        templateSlug: t.slug,
      }));
    }),
  );

  const merged = buildLists.flat();
  merged.sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
  return merged.slice(0, 50);
}

export default async function BuildsPage() {
  const data = await aggregateRecentBuilds();
  if (data === "unauthorized") redirect("/login");

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Builds</h1>
            <p className="text-sm text-zinc-500">
              Ultimos 50 builds agregados de todos los templates.
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
            <h2 className="text-xl font-semibold">Sin builds aun</h2>
            <p className="mt-2 text-sm text-zinc-500">
              Cuando dispares un webhook o un build manual desde un template, los
              ultimos apareceran aqui.
            </p>
          </div>
        )}

        {Array.isArray(data) && data.length > 0 && (
          <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Template</th>
                  <th className="px-4 py-3">Ref</th>
                  <th className="px-4 py-3">SHA</th>
                  <th className="px-4 py-3">Trigger</th>
                  <th className="px-4 py-3">Creado</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {data.map((b) => (
                  <tr key={b.id} className="hover:bg-zinc-900/60">
                    <td className="px-4 py-3">
                      <Link href={`/builds/${b.id}`} className="inline-block">
                        <StatusPill status={b.status} />
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        href={`/templates/${b.templateId}`}
                        className="text-sm text-zinc-200 hover:text-emerald-300"
                      >
                        {b.templateName}
                      </Link>
                      <div className="font-mono text-[10px] text-zinc-500">
                        {b.templateSlug}
                      </div>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-zinc-200">
                      {b.gitRef}
                    </td>
                    <td className="px-4 py-3 font-mono text-[11px] text-zinc-400">
                      {b.gitSha.slice(0, 8)}
                    </td>
                    <td className="px-4 py-3 text-xs text-zinc-400">
                      {b.trigger}
                    </td>
                    <td className="px-4 py-3 text-xs text-zinc-400">
                      {formatDate(b.createdAt)}
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
