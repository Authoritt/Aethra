"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { MonitorStatusChangedPayload } from "@/lib/types";

/**
 * Cliente SignalR ligero para el listado de monitores.
 */
export function MonitorsLive() {
  const router = useRouter();
  const [lastEvent, setLastEvent] =
    useState<MonitorStatusChangedPayload | null>(null);

  useEffect(() => {
    let cancelled = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on(
      "MonitorStatusChanged",
      (payload: MonitorStatusChangedPayload) => {
        if (cancelled) return;
        setLastEvent(payload);
        router.refresh();
      },
    );

    connection.start().catch(() => {});

    return () => {
      cancelled = true;
      connection.stop().catch(() => {});
    };
  }, [router]);

  const live = Boolean(lastEvent);
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium",
        live
          ? "border-success/30 bg-success/10 text-success-foreground"
          : "border-border bg-muted text-muted-foreground",
      )}
    >
      <span
        className={cn(
          "size-1.5 rounded-full",
          live ? "bg-success" : "bg-muted-foreground",
        )}
      />
      {live ? `último: ${lastEvent!.from} → ${lastEvent!.to}` : "en vivo"}
    </span>
  );
}
