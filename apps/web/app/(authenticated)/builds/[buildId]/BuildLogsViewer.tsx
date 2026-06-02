"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { LogsViewer, type LogEntry } from "@/components/aethra/logs-viewer";
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
 *  - Mantenemos un `cursor` numérico (seq del último chunk recibido).
 *  - GET /api/builds/{id}/logs?since=cursor anade chunks nuevos al final.
 *  - Cada poll tambien refresca el detail (GET /api/builds/{id}) para detectar
 *    transicion a status terminal y parar el polling.
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

        if (!terminal) {
          const detail = await api<BuildDetail>(
            `/api/builds/${encodeURIComponent(buildId)}`,
          );
          if (cancelled) return;
          if (TERMINAL_STATUSES.has(detail.status)) {
            setTerminal(true);
            return;
          }
        }
      } catch (e) {
        if (cancelled) return;
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string; detail?: string } | undefined)
                ?.message ?? `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : "Error desconocido";
        toast.error(msg);
      }

      if (!cancelled && !terminal) {
        timeoutId = setTimeout(tick, 2000);
      }
    }

    void tick();

    return () => {
      cancelled = true;
      if (timeoutId !== null) clearTimeout(timeoutId);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [buildId, terminal]);

  const entries: LogEntry[] = chunks.map((c) => ({
    sequence: c.seq,
    timestamp: c.timestamp,
    level: c.stream === "stderr" ? "error" : "info",
    text: c.line,
  }));

  return <LogsViewer entries={entries} isLive={!terminal} />;
}
