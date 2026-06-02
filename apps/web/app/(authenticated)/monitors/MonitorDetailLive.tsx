"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  MonitorStatus,
  MonitorStatusChangedPayload,
} from "@/lib/types";
import { MonitorStatusPill } from "./MonitorStatusPill";

interface Props {
  monitorId: string;
  initialStatus: MonitorStatus;
  initialLastCheckedAt: string | null;
  isEnabled: boolean;
}

export default function MonitorDetailLive({
  monitorId,
  initialStatus,
  initialLastCheckedAt,
  isEnabled,
}: Props) {
  const router = useRouter();
  const [status, setStatus] = useState<MonitorStatus>(initialStatus);
  const [lastCheckedAt, setLastCheckedAt] = useState<string | null>(
    initialLastCheckedAt,
  );
  const [lastLatency, setLastLatency] = useState<number | null>(null);
  const [connected, setConnected] = useState(false);

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
        if (payload.monitorId !== monitorId) return;
        setStatus(payload.to);
        setLastCheckedAt(payload.timestamp);
        setLastLatency(payload.latencyMs);
        router.refresh();
      },
    );

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
          // Si el hub no implementa JoinMonitor todavía
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
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Estado actual
        </CardTitle>
        <span
          className={cn(
            "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium",
            connected
              ? "border-success/30 bg-success/10 text-success-foreground"
              : "border-border bg-muted text-muted-foreground",
          )}
        >
          <span
            className={cn(
              "size-1.5 rounded-full",
              connected ? "bg-success" : "bg-muted-foreground",
            )}
          />
          {connected ? "hub en vivo" : "hub desconectado"}
        </span>
      </CardHeader>
      <CardContent>
        <div className="flex items-center gap-3">
          <MonitorStatusPill status={status} disabled={!isEnabled} />
          {lastLatency !== null ? (
            <span className="font-mono text-xs text-foreground">
              última: {lastLatency} ms
            </span>
          ) : null}
        </div>
        <div className="mt-2 text-xs text-muted-foreground">
          {lastCheckedAt
            ? `Último check: ${new Date(lastCheckedAt).toLocaleString()}`
            : "Sin checks aún"}
        </div>
      </CardContent>
    </Card>
  );
}
