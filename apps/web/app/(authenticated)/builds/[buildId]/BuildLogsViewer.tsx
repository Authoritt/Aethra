"use client";

import { useEffect, useRef, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { BuildDetail, BuildLogChunk } from "@/lib/types";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

/**
 * Visor de logs de build con polling cada 2s.
 *
 * Estrategia:
 *  - Mantenemos un `cursor` numerico (seq del ultimo chunk recibido).
 *  - GET /api/builds/{id}/logs?since=cursor anade chunks nuevos al final.
 *  - Cada poll tambien refresca el detail (GET /api/builds/{id}) para detectar
 *    transicion a status terminal y parar el polling.
 *  - Auto-scroll hasta el final mientras el usuario no haga scroll manual.
 */
export function BuildLogsViewer({
  buildId,
  terminal: initialTerminal,
}: {
  buildId: string;
  terminal: boolean;
}) {
  const [chunks, setChunks] = useState<BuildLogChunk[]>([]);
  const [cursor, setCursor] = useState(0);
  const [terminal, setTerminal] = useState(initialTerminal);
  const [error, setError] = useState<string | null>(null);
  const [autoFollow, setAutoFollow] = useState(true);
  const preRef = useRef<HTMLPreElement | null>(null);

  useEffect(() => {
    let cancelled = false;
    let timeoutId: ReturnType<typeof setTimeout> | null = null;

    async function tick() {
      try {
        const newChunks = await api<BuildLogChunk[]>(
          `/api/builds/${encodeURIComponent(buildId)}/logs?since=${cursor}`,
        );
        if (cancelled) return;
        if (Array.isArray(newChunks) && newChunks.length > 0) {
          const maxSeq = newChunks.reduce(
            (acc, c) => Math.max(acc, c.seq ?? 0),
            cursor,
          );
          setChunks((prev) => [...prev, ...newChunks]);
          setCursor(maxSeq);
        }

        // Refrescar status: si quedo terminal, dejamos de pollear.
        if (!terminal) {
          const detail = await api<BuildDetail>(
            `/api/builds/${encodeURIComponent(buildId)}`,
          );
          if (cancelled) return;
          if (TERMINAL_STATUSES.has(detail.status)) {
            setTerminal(true);
            // Una ultima lectura ya quedo en el ciclo previo; no agendamos
            // otro tick.
            return;
          }
        }
        setError(null);
      } catch (e) {
        if (cancelled) return;
        if (e instanceof ApiError) {
          const body = e.body as
            | { message?: string; detail?: string }
            | undefined;
          setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      }

      if (!cancelled && !terminal) {
        timeoutId = setTimeout(tick, 2000);
      }
    }

    // Ejecutamos el primer tick de inmediato; el segundo se agenda al final.
    void tick();

    return () => {
      cancelled = true;
      if (timeoutId !== null) clearTimeout(timeoutId);
    };
    // Re-ejecutamos cuando cambia el cursor para mantener el polling vivo.
    // El terminal flag corta el ciclo dentro del propio tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [buildId, terminal]);

  useEffect(() => {
    if (!autoFollow) return;
    const el = preRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [chunks, autoFollow]);

  function onScroll() {
    const el = preRef.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
    setAutoFollow(atBottom);
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between text-[11px] text-zinc-500">
        <span>
          {chunks.length} lineas · cursor seq {cursor}
        </span>
        <label className="flex items-center gap-1">
          <input
            type="checkbox"
            checked={autoFollow}
            onChange={(e) => setAutoFollow(e.target.checked)}
            className="size-3 accent-emerald-500"
          />
          Auto-follow
        </label>
      </div>
      <pre
        ref={preRef}
        onScroll={onScroll}
        className="max-h-[60vh] overflow-auto rounded-2xl border border-zinc-800 bg-zinc-950 px-4 py-3 font-mono text-[11px] leading-relaxed text-zinc-200"
      >
        {chunks.length === 0 ? (
          <span className="text-zinc-500">
            {terminal
              ? "Sin logs disponibles para este build."
              : "Esperando logs..."}
          </span>
        ) : (
          chunks.map((c) => (
            <div key={c.seq} className="whitespace-pre-wrap">
              <span className="text-zinc-600">{c.timestamp}</span>{" "}
              <span
                className={
                  c.stream === "stderr" ? "text-rose-300" : "text-zinc-200"
                }
              >
                {c.line}
              </span>
            </div>
          ))
        )}
      </pre>
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">
          {error}
        </p>
      )}
    </div>
  );
}
