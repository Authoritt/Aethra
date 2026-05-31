"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { API_URL } from "@/lib/api";
import type { MonitorStatus, MonitorStatusChangedPayload } from "@/lib/types";
import { MonitorStatusPill } from "./MonitorStatusPill";

interface Props {
  monitorId: string;
  initialStatus: MonitorStatus;
  initialLastCheckedAt: string | null;
  isEnabled: boolean;
}

/**
 * Card del estado en vivo del monitor de detalle. Se suscribe a <c>MonitorStatusChanged</c> en
 * el grupo del monitor y actualiza el pill + timestamp sin esperar a un refresh SSR. También
 * fuerza refresh periódico para que la tabla de checks se renueve.
 */
export default function MonitorDetailLive({
  monitorId,
  initialStatus,
  initialLastCheckedAt,
  isEnabled,
}: Props) {
  const router = useRouter();
  const [status, setStatus] = useState<MonitorStatus>(initialStatus);
  const [lastCheckedAt, setLastCheckedAt] = useState<string | null>(initialLastCheckedAt);
  const [lastLatency, setLastLatency] = useState<number | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("MonitorStatusChanged", (payload: MonitorStatusChangedPayload) => {
      if (cancelled) return;
      if (payload.monitorId !== monitorId) return;
      setStatus(payload.to);
      setLastCheckedAt(payload.timestamp);
      setLastLatency(payload.latencyMs);
      router.refresh();
    });

    connection.onreconnected(() => {
      setConnected(true);
      connection.invoke("JoinMonitor", monitorId).catch(() => {});
    });
    connection.onclose(() => setConnected(false));

    connection
      .start()
      .then(async () => {
        if (cancelled) return;
        setConnected(true);
        try {
          await connection.invoke("JoinMonitor", monitorId);
        } catch {
          // Si el hub no implementa JoinMonitor todavía, el grupo "all" sigue funcionando.
        }
      })
      .catch(() => {
        if (!cancelled) setConnected(false);
      });

    return () => {
      cancelled = true;
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke("LeaveMonitor", monitorId).catch(() => {});
      }
      connection.stop().catch(() => {});
    };
  }, [monitorId, router]);

  return (
    <section className="flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm uppercase tracking-wider text-zinc-500">
          Estado actual
        </h2>
        <span
          className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${
            connected
              ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-300"
              : "border-zinc-700 bg-zinc-800/40 text-zinc-400"
          }`}
        >
          <span
            className={`size-1.5 rounded-full ${
              connected ? "bg-emerald-400" : "bg-zinc-500"
            }`}
          />
          {connected ? "hub en vivo" : "hub desconectado"}
        </span>
      </div>

      <div className="flex items-center gap-3">
        <MonitorStatusPill status={status} disabled={!isEnabled} />
        {lastLatency !== null && (
          <span className="font-mono text-xs text-zinc-300">
            última: {lastLatency} ms
          </span>
        )}
      </div>
      <div className="text-xs text-zinc-500">
        {lastCheckedAt
          ? `Último check: ${new Date(lastCheckedAt).toLocaleString()}`
          : "Sin checks aún"}
      </div>
    </section>
  );
}
