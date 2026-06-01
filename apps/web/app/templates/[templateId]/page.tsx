import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  InstanceSummary,
  TemplateDetail,
} from "@/lib/types";
import { RotateWebhookSecretButton } from "./RotateWebhookSecretButton";

export const dynamic = "force-dynamic";

export default async function TemplateDetailPage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const { templateId } = await params;

  const [templateResult, instancesResult, buildsResult] = await Promise.all([
    serverFetch<TemplateDetail>(`/api/templates/${templateId}`),
    serverFetch<InstanceSummary[]>(`/api/templates/${templateId}/instances`),
    serverFetch<BuildSummary[]>(`/api/builds/templates/${templateId}`),
  ]);

  if (templateResult === "unauthorized") redirect("/login");
  if (templateResult === "notfound") notFound();
  if (templateResult === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el template.
        </div>
      </main>
    );
  }

  const template = templateResult;
  const instances = Array.isArray(instancesResult) ? instancesResult : [];
  const builds = Array.isArray(buildsResult) ? buildsResult.slice(0, 10) : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/projects" className="hover:text-zinc-300">
            Proyectos
          </Link>
          <span> / </span>
          <Link
            href={`/projects/${template.projectId}`}
            className="hover:text-zinc-300"
          >
            Proyecto
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{template.name}</span>
        </nav>

        <header className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="truncate text-3xl font-semibold">{template.name}</h1>
            <p className="mt-1 font-mono text-xs text-zinc-500">{template.slug}</p>
            {template.description && (
              <p className="mt-3 max-w-2xl text-sm text-zinc-300">
                {template.description}
              </p>
            )}
            <div className="mt-4 flex flex-wrap gap-2 text-[11px] uppercase tracking-wider text-zinc-500">
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                build: {template.build.buildType}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                instances {template.instanceCount}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                branch {template.source.branch}
              </span>
            </div>
          </div>
          <div className="flex shrink-0 gap-2">
            <RotateWebhookSecretButton templateId={template.id} />
            <Link
              href={`/templates/${template.id}/instances/new`}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              Crear instance
            </Link>
          </div>
        </header>

        <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Card title="Source">
            <Kv label="Git repo" value={template.source.gitRepoUrl} mono />
            <Kv label="Branch" value={template.source.branch} mono />
            <Kv label="Base directory" value={template.source.baseDirectory} mono />
            <div>
              <dt className="text-xs uppercase tracking-wider text-zinc-500">
                Watch paths
              </dt>
              <dd className="mt-1 flex flex-wrap gap-1">
                {template.source.watchPaths.map((p) => (
                  <span
                    key={p}
                    className="rounded border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-[10px] text-zinc-300"
                  >
                    {p}
                  </span>
                ))}
              </dd>
            </div>
          </Card>

          <Card title="Build">
            <Kv label="Tipo" value={template.build.buildType} />
            {template.build.dockerfilePath && (
              <Kv label="Dockerfile" value={template.build.dockerfilePath} mono />
            )}
            {template.build.composeFilePath && (
              <Kv
                label="Compose file"
                value={template.build.composeFilePath}
                mono
              />
            )}
            <div>
              <dt className="text-xs uppercase tracking-wider text-zinc-500">
                Build args
              </dt>
              <dd className="mt-1">
                {template.build.buildArgs.length === 0 ? (
                  <span className="text-xs text-zinc-500">sin args</span>
                ) : (
                  <ul className="flex flex-col gap-1 font-mono text-[11px]">
                    {template.build.buildArgs.map((a) => (
                      <li
                        key={a.key}
                        className="rounded border border-zinc-800 bg-zinc-950 px-2 py-1 text-zinc-300"
                      >
                        <span className="text-emerald-300">{a.key}</span>
                        <span className="text-zinc-600">=</span>
                        <span className="text-zinc-200">{a.value}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </dd>
            </div>
          </Card>
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
              Aun no hay instances. Crea la primera para desplegar este template
              hacia un client + environment.
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
                    <p className="mt-2 truncate font-mono text-[11px] text-zinc-400">
                      {inst.customDomain ?? inst.autoHostname ?? "sin hostname"}
                    </p>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Builds recientes{" "}
            <span className="ml-2 font-mono text-[11px] text-zinc-600">
              {builds.length}
            </span>
            {buildsResult === "error" && (
              <span className="ml-2 text-[11px] normal-case text-rose-400">
                (error al cargar)
              </span>
            )}
          </h2>
          {builds.length === 0 ? (
            <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
              Sin builds aun. Cuando dispares un webhook o build manual, los
              ultimos 10 apareceran aqui.
            </p>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
              <table className="w-full text-left text-sm">
                <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                  <tr>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3">Ref</th>
                    <th className="px-4 py-3">SHA</th>
                    <th className="px-4 py-3">Trigger</th>
                    <th className="px-4 py-3">Creado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800">
                  {builds.map((b) => (
                    <tr
                      key={b.id}
                      className="cursor-pointer transition hover:bg-zinc-900/60"
                    >
                      <td className="px-4 py-3">
                        <Link
                          href={`/builds/${b.id}`}
                          className="inline-block"
                        >
                          <StatusPill status={b.status} />
                        </Link>
                      </td>
                      <td className="px-4 py-3">
                        <Link
                          href={`/builds/${b.id}`}
                          className="font-mono text-xs text-zinc-200 hover:text-emerald-300"
                        >
                          {b.gitRef}
                        </Link>
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
        </section>
      </div>
    </main>
  );
}

function Card({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
      <h3 className="text-xs uppercase tracking-wider text-zinc-500">{title}</h3>
      <dl className="mt-3 flex flex-col gap-3">{children}</dl>
    </div>
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
