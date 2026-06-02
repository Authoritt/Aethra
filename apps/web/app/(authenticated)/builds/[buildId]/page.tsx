import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { StatusPill } from "@/app/_components/StatusPill";
import { serverFetch } from "@/lib/server-fetch";
import type { BuildDetail } from "@/lib/types";
import { BuildLogsViewer } from "./BuildLogsViewer";

export const dynamic = "force-dynamic";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

export default async function BuildDetailPage({
  params,
}: {
  params: Promise<{ buildId: string }>;
}) {
  const { buildId } = await params;
  const build = await serverFetch<BuildDetail>(`/api/builds/${buildId}`);
  if (build === "unauthorized") redirect("/login");
  if (build === "notfound") notFound();
  if (build === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el build.
        </div>
      </main>
    );
  }

  const terminal = TERMINAL_STATUSES.has(build.status);

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link href="/builds" className="hover:text-zinc-300">
            Builds
          </Link>
          <span> / </span>
          <Link
            href={`/templates/${build.templateId}`}
            className="hover:text-zinc-300"
          >
            Template
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{build.id.slice(0, 8)}</span>
        </nav>

        <header className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-3">
              <StatusPill status={build.status} />
              <h1 className="font-mono text-2xl font-semibold">
                {build.gitSha.slice(0, 12)}
              </h1>
            </div>
            <p className="mt-2 font-mono text-xs text-zinc-500">{build.id}</p>
            <div className="mt-3 flex flex-wrap gap-2 text-[11px] uppercase tracking-wider text-zinc-500">
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                ref {build.gitRef}
              </span>
              <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                trigger {build.trigger}
              </span>
              {build.triggeredBy && (
                <span className="rounded border border-zinc-800 bg-zinc-900/60 px-2 py-0.5">
                  by {build.triggeredBy}
                </span>
              )}
            </div>
          </div>
        </header>

        <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Timing
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv label="Creado" value={formatDate(build.createdAt)} />
              <Kv
                label="Inicio"
                value={build.startedAt ? formatDate(build.startedAt) : "—"}
              />
              <Kv
                label="Fin"
                value={build.finishedAt ? formatDate(build.finishedAt) : "—"}
              />
              <Kv
                label="Duracion"
                value={
                  build.buildDurationMs != null
                    ? formatDuration(build.buildDurationMs)
                    : "—"
                }
              />
            </dl>
          </div>
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h3 className="text-xs uppercase tracking-wider text-zinc-500">
              Resultado
            </h3>
            <dl className="mt-3 flex flex-col gap-2 text-sm">
              <Kv label="Image ref" value={build.imageRef ?? "—"} mono />
              <Kv label="Error code" value={build.errorCode ?? "—"} mono />
              <Kv
                label="Error message"
                value={build.errorMessage ?? "—"}
                mono={Boolean(build.errorMessage)}
              />
            </dl>
          </div>
        </section>

        <section className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm uppercase tracking-wider text-zinc-500">
              Logs
            </h2>
            {terminal ? (
              <span className="text-[11px] text-zinc-500">build terminado</span>
            ) : (
              <span className="text-[11px] text-emerald-300">
                streaming (polling cada 2s)
              </span>
            )}
          </div>
          <BuildLogsViewer buildId={build.id} terminal={terminal} />
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

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}
