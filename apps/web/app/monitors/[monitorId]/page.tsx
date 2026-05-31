import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { MonitorCheckDto, MonitorDetailDto } from "@/lib/types";
import MonitorLatencyChart from "../MonitorLatencyChart";
import { CheckHistoryTable } from "../CheckHistoryTable";
import { TriggerCheckButton } from "../TriggerCheckButton";
import { EnableDisableButtons } from "../EnableDisableButtons";
import { DeleteMonitorButton } from "../DeleteMonitorButton";
import MonitorDetailLive from "../MonitorDetailLive";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchMonitor(
  monitorId: string,
): Promise<MonitorDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/monitors/${monitorId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as MonitorDetailDto;
}

async function fetchChecks(monitorId: string): Promise<MonitorCheckDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(
    `${API_URL}/api/monitors/${monitorId}/checks?limit=100`,
    { headers: { cookie: cookieHeader }, cache: "no-store" },
  );
  if (!res.ok) return [];
  return (await res.json()) as MonitorCheckDto[];
}

export default async function MonitorDetailPage({
  params,
}: {
  params: Promise<{ monitorId: string }>;
}) {
  const { monitorId } = await params;
  const data = await fetchMonitor(monitorId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el monitor.
        </div>
      </main>
    );
  }

  const monitor = data;
  const checks = await fetchChecks(monitorId);

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/monitors" className="hover:text-zinc-300">
            Monitores
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{monitor.name}</span>
        </nav>

        <header className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="truncate text-3xl font-semibold">{monitor.name}</h1>
            <p className="mt-1 font-mono text-xs text-zinc-500">{monitor.slug}</p>
            <p className="mt-2 break-all font-mono text-sm text-zinc-300">
              {monitor.http_method} {monitor.url}
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <TriggerCheckButton monitorId={monitor.id} />
            <EnableDisableButtons
              monitorId={monitor.id}
              isEnabled={monitor.is_enabled}
            />
            <div className="flex items-center gap-2">
              <Link
                href={`/monitors/${monitor.id}/edit`}
                className="rounded-full border border-zinc-700 px-4 py-1.5 text-xs font-medium text-zinc-300 transition hover:bg-zinc-800"
              >
                Editar
              </Link>
              <DeleteMonitorButton monitorId={monitor.id} name={monitor.name} />
            </div>
          </div>
        </header>

        <MonitorDetailLive
          monitorId={monitor.id}
          initialStatus={monitor.status}
          initialLastCheckedAt={monitor.last_checked_at}
          isEnabled={monitor.is_enabled}
        />

        <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <InfoCard label="Intervalo" value={`${monitor.interval_sec}s`} />
          <InfoCard label="Timeout" value={`${monitor.timeout_ms}ms`} />
          <InfoCard
            label="OK esperado"
            value={monitor.expected_status_codes.join(", ")}
            mono
          />
          <InfoCard
            label="Fallos seguidos"
            value={String(monitor.consecutive_failures)}
            mono
          />
        </section>

        <section>
          <h2 className="mb-3 text-sm uppercase tracking-wider text-zinc-500">
            Latencia (últimos {checks.length} checks)
          </h2>
          <MonitorLatencyChart checks={checks} />
        </section>

        {(monitor.headers || monitor.body_template) && (
          <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
            <h2 className="mb-3 text-sm uppercase tracking-wider text-zinc-500">
              Request
            </h2>
            {monitor.headers && Object.keys(monitor.headers).length > 0 && (
              <div className="mb-3">
                <h3 className="text-xs uppercase text-zinc-500">Headers</h3>
                <dl className="mt-1 grid grid-cols-1 gap-1 font-mono text-xs">
                  {Object.entries(monitor.headers).map(([k, v]) => (
                    <div key={k} className="flex gap-2">
                      <dt className="text-zinc-500">{k}:</dt>
                      <dd className="break-all text-zinc-300">{v}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            )}
            {monitor.body_template && (
              <div>
                <h3 className="text-xs uppercase text-zinc-500">Body</h3>
                <pre className="mt-1 whitespace-pre-wrap break-all rounded-lg bg-zinc-950 p-3 font-mono text-xs text-zinc-300">
                  {monitor.body_template}
                </pre>
              </div>
            )}
          </section>
        )}

        <section>
          <h2 className="mb-3 text-sm uppercase tracking-wider text-zinc-500">
            Historial reciente
          </h2>
          <CheckHistoryTable checks={checks} />
        </section>
      </div>
    </main>
  );
}

function InfoCard({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-xl border border-zinc-800 bg-zinc-900/40 p-4">
      <div className="text-[10px] uppercase tracking-wider text-zinc-500">
        {label}
      </div>
      <div className={`mt-1 text-sm text-zinc-200 ${mono ? "font-mono" : ""}`}>
        {value}
      </div>
    </div>
  );
}
