"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { API_URL } from "@/lib/api";
import type { MonitorStatusChangedPayload } from "@/lib/types";

/**
 * Cliente SignalR ligero para el listado de monitores. Se suscribe al hub y, ante un evento
 * <c>MonitorStatusChanged</c>, fuerza un <c>router.refresh()</c> para volver a tirar el SSR.
 *
 * <para>
 * Alternativa más fina: parchar el state local fila a fila. Para F6 el refresh global es
 * suficiente — la lista cabe en pantalla y rehidratarla cuesta milisegundos.
 * </para>
 */
export function MonitorsLive() {
  const router = useRouter();
  const [lastEvent, setLastEvent] = useState<MonitorStatusChangedPayload | null>(null);

  useEffect(() => {
    let cancelled = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("MonitorStatusChanged", (payload: MonitorStatusChangedPayload) => {
      if (cancelled) return;
      setLastEvent(payload);
      router.refresh();
    });

    connection.start().catch(() => {
      // El hub aún puede no estar disponible (auth, BD); el refresh manual sigue funcionando.
    });

    return () => {
      cancelled = true;
      connection.stop().catch(() => {});
    };
  }, [router]);

  if (!lastEvent) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full border border-zinc-700 bg-zinc-800/40 px-2.5 py-0.5 text-[11px] font-medium text-zinc-400">
        <span className="size-1.5 rounded-full bg-zinc-500" />
        en vivo
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2.5 py-0.5 text-[11px] font-medium text-emerald-300">
      <span className="size-1.5 rounded-full bg-emerald-400" />
      último: {lastEvent.from} → {lastEvent.to}
    </span>
  );
}
