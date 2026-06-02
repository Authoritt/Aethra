import type { MonitorCheckDto } from "@/lib/types";
import { MonitorStatusPill } from "./MonitorStatusPill";

interface Props {
  checks: MonitorCheckDto[];
}

/**
 * Historial reciente (más nuevo primero). Espera <c>checks</c> en orden cronológico
 * ascendente (como vuelve la API) — se invierte localmente para mostrar.
 */
export function CheckHistoryTable({ checks }: Props) {
  if (checks.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-6 text-center text-sm text-zinc-500">
        Sin checks registrados.
      </div>
    );
  }
  const newestFirst = [...checks].reverse();
  return (
    <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
      <table className="w-full text-left text-sm">
        <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
          <tr>
            <th className="px-4 py-2">Cuando</th>
            <th className="px-4 py-2">Estado</th>
            <th className="px-4 py-2">HTTP</th>
            <th className="px-4 py-2">Latencia</th>
            <th className="px-4 py-2">Detalle</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-zinc-800">
          {newestFirst.map((c) => (
            <tr key={c.id} className="hover:bg-zinc-900/60">
              <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-zinc-300">
                {formatStamp(c.timestamp)}
              </td>
              <td className="px-4 py-2">
                <MonitorStatusPill status={c.status} />
              </td>
              <td className="px-4 py-2 font-mono text-xs text-zinc-300">
                {c.http_status_code ?? "—"}
              </td>
              <td className="px-4 py-2 font-mono text-xs text-zinc-300">
                {c.latency_ms === null ? "—" : `${c.latency_ms} ms`}
              </td>
              <td className="px-4 py-2 text-xs text-zinc-400">
                {c.error_message ? (
                  <span className="text-rose-300">{c.error_message}</span>
                ) : c.response_snippet ? (
                  <span title={c.response_snippet} className="line-clamp-1">
                    {c.response_snippet}
                  </span>
                ) : (
                  "—"
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatStamp(iso: string): string {
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
