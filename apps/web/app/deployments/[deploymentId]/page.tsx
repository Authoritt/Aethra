import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type { DeploymentDetail } from "@/lib/types";
import { DeploymentLivePoll } from "./DeploymentLivePoll";

export const dynamic = "force-dynamic";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

export default async function DeploymentDetailPage({
  params,
}: {
  params: Promise<{ deploymentId: string }>;
}) {
  const { deploymentId } = await params;
  const deployment = await serverFetch<DeploymentDetail>(
    `/api/deployments/${deploymentId}`,
  );
  if (deployment === "unauthorized") redirect("/login");
  if (deployment === "notfound") notFound();
  if (deployment === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el deployment.
        </div>
      </main>
    );
  }

  const terminal = TERMINAL_STATUSES.has(deployment.status);

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link href="/deployments" className="hover:text-zinc-300">
            Deployments
          </Link>
          <span> / </span>
          <Link
            href={`/instances/${deployment.instanceId}`}
            className="hover:text-zinc-300"
          >
            Instance
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{deployment.id.slice(0, 8)}</span>
        </nav>

        <header className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-3">
              <StatusPill status={deployment.status} />
              <h1 className="font-mono text-2xl font-semibold">
                {deployment.id.slice(0, 12)}
              </h1>
            </div>
            <p className="mt-2 font-mono text-xs text-zinc-500">{deployment.id}</p>
            <div className="mt-3 flex flex-wrap gap-2 text-[11px] uppercase tracking-wider text-zinc-500">
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                trigger {deployment.trigger}
              </span>
            </div>
          </div>
          {!terminal && (
            <DeploymentLivePoll deploymentId={deployment.id} />
          )}
        </header>

        <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Timing
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv label="Creado" value={formatDate(deployment.createdAt)} />
              <Kv
                label="Fin"
                value={
                  deployment.finishedAt
                    ? formatDate(deployment.finishedAt)
                    : "—"
                }
              />
            </dl>
          </div>
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Imagen
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv label="Nueva" value={deployment.newImageRef} mono />
              <Kv
                label="Anterior"
                value={deployment.oldImageRef ?? "—"}
                mono={Boolean(deployment.oldImageRef)}
              />
            </dl>
          </div>
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Container
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv
                label="Nuevo"
                value={deployment.newContainerId ?? "—"}
                mono={Boolean(deployment.newContainerId)}
              />
              <Kv
                label="Anterior"
                value={deployment.oldContainerId ?? "—"}
                mono={Boolean(deployment.oldContainerId)}
              />
            </dl>
          </div>
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Resultado
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv label="Error code" value={deployment.errorCode ?? "—"} mono />
              <Kv
                label="Error message"
                value={deployment.errorMessage ?? "—"}
                mono={Boolean(deployment.errorMessage)}
              />
            </dl>
          </div>
        </section>

        <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
          <h3 className="text-xs uppercase tracking-wider text-zinc-500">
            Logs del deploy
          </h3>
          <p className="mt-2 text-sm text-zinc-400">
            El detalle del deploy no expone logs propios en el contrato actual.
            Para ver los logs del build asociado y entender el origen de la
            imagen,{" "}
            <Link
              href={`/builds/${deployment.buildId}`}
              className="text-emerald-300 underline-offset-2 hover:underline"
            >
              abre el build {deployment.buildId.slice(0, 8)}
            </Link>
            .
          </p>
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

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
