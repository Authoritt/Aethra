import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  DeploymentSummary,
  InstanceDetail,
  TemplateDetail,
} from "@/lib/types";
import { AutoDeployToggle } from "./AutoDeployToggle";
import { CustomDomainForm } from "./CustomDomainForm";
import { DeployBuildButton } from "./DeployBuildButton";

export const dynamic = "force-dynamic";

export default async function InstanceDetailPage({
  params,
}: {
  params: Promise<{ instanceId: string }>;
}) {
  const { instanceId } = await params;

  const instanceResult = await serverFetch<InstanceDetail>(
    `/api/instances/${instanceId}`,
  );
  if (instanceResult === "unauthorized") redirect("/login");
  if (instanceResult === "notfound") notFound();
  if (instanceResult === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando la instance.
        </div>
      </main>
    );
  }
  const instance = instanceResult;

  const [deploymentsResult, templateResult] = await Promise.all([
    serverFetch<DeploymentSummary[]>(
      `/api/deployments/instances/${instance.id}`,
    ),
    serverFetch<TemplateDetail>(`/api/templates/${instance.templateId}`),
  ]);

  const deployments = Array.isArray(deploymentsResult)
    ? deploymentsResult.slice(0, 10)
    : [];

  let buildsResult:
    | Awaited<ReturnType<typeof serverFetch<BuildSummary[]>>>
    | null = null;
  if (
    templateResult !== "unauthorized" &&
    templateResult !== "notfound" &&
    templateResult !== "error"
  ) {
    buildsResult = await serverFetch<BuildSummary[]>(
      `/api/builds/templates/${instance.templateId}`,
    );
  }
  const builds = Array.isArray(buildsResult) ? buildsResult.slice(0, 10) : [];

  const effectiveHost = instance.customDomain ?? instance.autoHostname;
  const openUrl = effectiveHost ? `https://${effectiveHost}` : null;

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link
            href={`/templates/${instance.templateId}`}
            className="hover:text-zinc-300"
          >
            Template
          </Link>
          <span> / </span>
          <Link
            href={`/clients/${instance.clientId}`}
            className="hover:text-zinc-300"
          >
            Client
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{instance.slug}</span>
        </nav>

        <header className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="truncate font-mono text-2xl font-semibold">
              {instance.slug}
            </h1>
            <p className="mt-1 text-xs text-zinc-500">
              container <span className="font-mono">{instance.containerName}</span>
            </p>
            <div className="mt-3 flex flex-wrap gap-2 text-[11px] uppercase tracking-wider text-zinc-500">
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                env {instance.environment}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                vm {instance.targetVmId.slice(0, 8)}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                ports {instance.ports.length}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                volumes {instance.volumes.length}
              </span>
            </div>
          </div>
        </header>

        <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-4 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-sm uppercase tracking-wider text-zinc-500">
                Hostname & routing
              </h2>
              {openUrl ? (
                <a
                  href={openUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                  className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800"
                >
                  Abrir
                </a>
              ) : (
                <span className="rounded-full border border-zinc-800 bg-zinc-900/40 px-3 py-1 text-xs text-zinc-500">
                  sin host
                </span>
              )}
            </div>
            <div className="flex flex-col gap-2">
              <span className="text-xs uppercase tracking-wider text-zinc-500">
                Auto-hostname
              </span>
              <AutoHostnameInfo
                autoHostname={instance.autoHostname}
                customDomain={instance.customDomain}
              />
            </div>
            <CustomDomainForm
              instanceId={instance.id}
              initialDomain={instance.customDomain}
            />
          </div>

          <div className="flex flex-col gap-4 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h2 className="text-sm uppercase tracking-wider text-zinc-500">
              Auto-deploy
            </h2>
            <AutoDeployToggle
              instanceId={instance.id}
              initial={instance.autoDeployOnNewBuild}
            />
            <p className="text-[11px] text-zinc-500">
              Cuando esta activo, cada nuevo build verde del template padre
              dispara automaticamente un deploy aqui. Desactivalo para
              promociones manuales o ventanas de mantenimiento.
            </p>
          </div>
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Deployments recientes{" "}
            <span className="ml-2 font-mono text-[11px] text-zinc-600">
              {deployments.length}
            </span>
            {deploymentsResult === "error" && (
              <span className="ml-2 text-[11px] normal-case text-rose-400">
                (error al cargar)
              </span>
            )}
          </h2>
          {deployments.length === 0 ? (
            <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
              Esta instance aun no se ha desplegado.
            </p>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
              <table className="w-full text-left text-sm">
                <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                  <tr>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3">Trigger</th>
                    <th className="px-4 py-3">Build</th>
                    <th className="px-4 py-3">Creado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800">
                  {deployments.map((d) => (
                    <tr key={d.id} className="hover:bg-zinc-900/60">
                      <td className="px-4 py-3">
                        <Link href={`/deployments/${d.id}`} className="inline-block">
                          <StatusPill status={d.status} />
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-xs text-zinc-400">
                        {d.trigger}
                      </td>
                      <td className="px-4 py-3">
                        <Link
                          href={`/builds/${d.buildId}`}
                          className="font-mono text-[11px] text-zinc-300 hover:text-emerald-300"
                        >
                          {d.buildId.slice(0, 8)}
                        </Link>
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
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Builds disponibles del template{" "}
            <span className="ml-2 font-mono text-[11px] text-zinc-600">
              {builds.length}
            </span>
          </h2>
          {builds.length === 0 ? (
            <p className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-400">
              Sin builds del template padre. Cuando haya uno verde podras
              desplegarlo aqui.
            </p>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
              <table className="w-full text-left text-sm">
                <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                  <tr>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3">Ref</th>
                    <th className="px-4 py-3">SHA</th>
                    <th className="px-4 py-3">Image</th>
                    <th className="px-4 py-3 text-right">Accion</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800">
                  {builds.map((b) => {
                    const deployable = Boolean(b.imageRef) && b.status === "Completed";
                    return (
                      <tr key={b.id} className="hover:bg-zinc-900/60">
                        <td className="px-4 py-3">
                          <Link href={`/builds/${b.id}`} className="inline-block">
                            <StatusPill status={b.status} />
                          </Link>
                        </td>
                        <td className="px-4 py-3 font-mono text-xs text-zinc-200">
                          {b.gitRef}
                        </td>
                        <td className="px-4 py-3 font-mono text-[11px] text-zinc-400">
                          {b.gitSha.slice(0, 8)}
                        </td>
                        <td className="px-4 py-3 font-mono text-[11px] text-zinc-400">
                          {b.imageRef ?? "—"}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <DeployBuildButton
                            buildId={b.id}
                            instanceId={instance.id}
                            disabled={!deployable}
                          />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">Ports</h3>
            {instance.ports.length === 0 ? (
              <p className="mt-2 text-sm text-zinc-500">Sin puertos.</p>
            ) : (
              <ul className="mt-2 flex flex-col gap-1 font-mono text-[11px] text-zinc-300">
                {instance.ports.map((p, i) => (
                  <li
                    key={i}
                    className="rounded border border-zinc-800 bg-zinc-950 px-2 py-1"
                  >
                    {p.containerPort} {"->"} {p.hostPort ?? "auto"}{" "}
                    <span className="text-zinc-500">/{p.protocol.toLowerCase()}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Volumes
            </h3>
            {instance.volumes.length === 0 ? (
              <p className="mt-2 text-sm text-zinc-500">Sin volumes.</p>
            ) : (
              <ul className="mt-2 flex flex-col gap-1 font-mono text-[11px] text-zinc-300">
                {instance.volumes.map((v, i) => (
                  <li
                    key={i}
                    className="rounded border border-zinc-800 bg-zinc-950 px-2 py-1"
                  >
                    <span className="text-emerald-300">{v.name}</span>
                    <span className="text-zinc-500"> {"->"} </span>
                    {v.containerPath}
                    {v.readOnly && (
                      <span className="ml-2 text-[10px] uppercase tracking-wider text-zinc-500">
                        ro
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
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
