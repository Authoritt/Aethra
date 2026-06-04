"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricsChart } from "@/components/aethra/metrics-chart";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { VmMetricPoint, VmStatus } from "@/lib/types";

const MAX_POINTS = 60;

type ConnectionPhase =
  | "idle"
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected"
  | "error";

interface Props {
  vmId: string;
  initialStatus: VmStatus;
  initialMetrics: VmMetricPoint[];
  totalMemoryBytes: number | null;
}

export default function VmLiveDashboard({
  vmId,
  initialStatus,
  initialMetrics,
  totalMemoryBytes,
}: Props) {
  const seed = useMemo(
    () => [...initialMetrics].reverse().slice(-MAX_POINTS),
    [initialMetrics],
  );

  const [points, setPoints] = useState<VmMetricPoint[]>(seed);
  const [status, setStatus] = useState<VmStatus>(initialStatus);
  const [phase, setPhase] = useState<ConnectionPhase>("idle");
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let cancelled = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, {
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on(
      "VmMetricsUpdated",
      (incomingVmId: string, snapshot: VmMetricPoint) => {
        if (incomingVmId !== vmId) return;
        setPoints((prev) => {
          const next = [...prev, snapshot];
          if (next.length > MAX_POINTS)
            next.splice(0, next.length - MAX_POINTS);
          return next;
        });
      },
    );

    connection.on(
      "VmStatusChanged",
      (incomingVmId: string, newStatus: VmStatus) => {
        if (incomingVmId !== vmId) return;
        setStatus(newStatus);
      },
    );

    connection.onreconnecting(() => setPhase("reconnecting"));
    connection.onreconnected(() => {
      setPhase("connected");
      connection.invoke("JoinVm", vmId).catch(() => {});
    });
    connection.onclose(() => setPhase("disconnected"));

    setPhase("connecting");
    connection
      .start()
      .then(async () => {
        if (cancelled) return;
        setPhase("connected");
        try {
          await connection.invoke("JoinVm", vmId);
        } catch {
          // hub aún sin método o sin permiso — no es fatal para el render
        }
      })
      .catch(() => {
        if (!cancelled) setPhase("error");
      });

    return () => {
      cancelled = true;
      const c = connectionRef.current;
      connectionRef.current = null;
      if (c) {
        if (c.state === HubConnectionState.Connected) {
          c.invoke("LeaveVm", vmId).catch(() => {});
        }
        c.stop().catch(() => {});
      }
    };
  }, [vmId]);

  const latest = points.length > 0 ? points[points.length - 1] : null;
  const memoryTotal = latest?.memoryTotalBytes ?? totalMemoryBytes ?? 0;
  const memoryUsed = latest?.memoryUsedBytes ?? 0;
  const memoryPct = memoryTotal > 0 ? (memoryUsed / memoryTotal) * 100 : 0;
  const diskTotal = latest?.diskTotalBytes ?? 0;
  const diskUsed = latest?.diskUsedBytes ?? 0;
  const diskFree = Math.max(0, diskTotal - diskUsed);
  const diskPct = diskTotal > 0 ? (diskUsed / diskTotal) * 100 : 0;
  const cpuPct = latest?.cpuPercent ?? 0;
  const netRx = latest?.netBytesReceived ?? 0;
  const netTx = latest?.netBytesSent ?? 0;

  const chartData = useMemo(
    () =>
      points.map((p) => ({
        timestamp: p.timestamp,
        cpu: round1(p.cpuPercent),
        ram:
          p.memoryTotalBytes > 0
            ? round1((p.memoryUsedBytes / p.memoryTotalBytes) * 100)
            : 0,
        disk:
          p.diskTotalBytes > 0
            ? round1((p.diskUsedBytes / p.diskTotalBytes) * 100)
            : 0,
      })),
    [points],
  );

  return (
    <section className="flex flex-col gap-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Métricas en vivo
        </h2>
        <ConnectionBadge phase={phase} status={status} />
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4">
        <BigStat label="CPU" value={`${round1(cpuPct)}%`} bar={cpuPct} tone="info" />
        <BigStat
          label="RAM"
          value={`${round1(memoryPct)}%`}
          sub={
            memoryTotal > 0
              ? `${formatBytes(memoryUsed)} / ${formatBytes(memoryTotal)}`
              : "—"
          }
          bar={memoryPct}
          tone="primary"
        />
        <BigStat
          label="Disco"
          value={`${round1(diskPct)}%`}
          sub={
            diskTotal > 0
              ? `${formatBytes(diskFree)} libres de ${formatBytes(diskTotal)}`
              : "—"
          }
          bar={diskPct}
          tone={diskPct >= 90 ? "destructive" : diskPct >= 75 ? "warning" : "success"}
        />
        <BigStat
          label="Red"
          value={`${formatBytes(netRx)} ↓  ${formatBytes(netTx)} ↑`}
          sub="acumulado del satélite"
        />
      </div>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle className="text-base">
            CPU · RAM · Disco (%) — últimos {MAX_POINTS} puntos
          </CardTitle>
          <span className="text-xs text-muted-foreground">
            {points.length} muestras
          </span>
        </CardHeader>
        <CardContent>
          {chartData.length === 0 ? (
            <div className="flex h-56 items-center justify-center text-sm text-muted-foreground">
              Esperando muestras del satélite…
            </div>
          ) : (
            <MetricsChart
              data={chartData}
              series={[
                { dataKey: "cpu", label: "CPU", tone: "info" },
                { dataKey: "ram", label: "RAM", tone: "primary" },
                { dataKey: "disk", label: "Disco", tone: "warning" },
              ]}
              variant="line"
              formatValue={(v) => `${v}%`}
              height={224}
            />
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function BigStat({
  label,
  value,
  sub,
  tone = "primary",
  bar,
}: {
  label: string;
  value: string;
  sub?: string;
  tone?: "primary" | "info" | "success" | "warning" | "destructive";
  bar?: number;
}) {
  const barColor = {
    primary: "bg-primary",
    info: "bg-info",
    success: "bg-success",
    warning: "bg-warning",
    destructive: "bg-destructive",
  }[tone];
  const pct = typeof bar === "number" ? Math.max(0, Math.min(100, bar)) : null;

  return (
    <Card>
      <CardContent className="p-5">
        <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
          {label}
        </div>
        <div className="mt-2 text-3xl font-semibold text-foreground">
          {value}
        </div>
        {sub ? (
          <div className="mt-1 text-xs text-muted-foreground">{sub}</div>
        ) : null}
        {pct !== null ? (
          <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-muted">
            <div
              className={cn("h-full transition-all duration-500", barColor)}
              style={{ width: `${pct}%` }}
            />
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function ConnectionBadge({
  phase,
  status,
}: {
  phase: ConnectionPhase;
  status: VmStatus;
}) {
  const map: Record<
    ConnectionPhase,
    { label: string; cls: string; dot: string }
  > = {
    idle: {
      label: "Esperando",
      cls: "border-border bg-muted text-muted-foreground",
      dot: "bg-muted-foreground",
    },
    connecting: {
      label: "Conectando hub…",
      cls: "border-border bg-muted text-foreground",
      dot: "bg-muted-foreground animate-pulse",
    },
    connected: {
      label: `Hub en vivo · VM ${status}`,
      cls: "border-success/30 bg-success/10 text-success-foreground",
      dot: "bg-success",
    },
    reconnecting: {
      label: "Reconectando…",
      cls: "border-warning/30 bg-warning/10 text-warning-foreground",
      dot: "bg-warning animate-pulse",
    },
    disconnected: {
      label: "Hub desconectado",
      cls: "border-destructive/30 bg-destructive/10 text-destructive-foreground",
      dot: "bg-destructive",
    },
    error: {
      label: "Error de hub",
      cls: "border-destructive/30 bg-destructive/10 text-destructive-foreground",
      dot: "bg-destructive",
    },
  };
  const v = map[phase];
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium",
        v.cls,
      )}
    >
      <span className={cn("size-1.5 rounded-full", v.dot)} />
      {v.label}
    </span>
  );
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  let n = bytes;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return `${n.toFixed(n >= 100 || i === 0 ? 0 : 1)} ${units[i]}`;
}

function round1(n: number): number {
  return Math.round(n * 10) / 10;
}
