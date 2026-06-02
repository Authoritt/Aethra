"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { API_URL } from "@/lib/api";
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
  // Most recent first in API; we want chronological for the chart.
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

    connection.on("VmMetricsUpdated", (incomingVmId: string, snapshot: VmMetricPoint) => {
      if (incomingVmId !== vmId) return;
      setPoints((prev) => {
        const next = [...prev, snapshot];
        if (next.length > MAX_POINTS) next.splice(0, next.length - MAX_POINTS);
        return next;
      });
    });

    connection.on("VmStatusChanged", (incomingVmId: string, newStatus: VmStatus) => {
      if (incomingVmId !== vmId) return;
      setStatus(newStatus);
    });

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
  const memoryTotal =
    latest?.memory_total_bytes ?? totalMemoryBytes ?? 0;
  const memoryUsed = latest?.memory_used_bytes ?? 0;
  const memoryPct = memoryTotal > 0 ? (memoryUsed / memoryTotal) * 100 : 0;
  const cpuPct = latest?.cpu_percent ?? 0;
  const netRx = latest?.net_bytes_received ?? 0;
  const netTx = latest?.net_bytes_sent ?? 0;

  const chartData = useMemo(
    () =>
      points.map((p) => ({
        t: formatTimeLabel(p.timestamp),
        cpu: round1(p.cpu_percent),
      })),
    [points],
  );

  return (
    <section className="flex flex-col gap-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm uppercase tracking-wider text-zinc-500">
          Métricas en vivo
        </h2>
        <ConnectionBadge phase={phase} status={status} />
      </div>

      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <BigStat
          label="CPU"
          value={`${round1(cpuPct)}%`}
          accent="emerald"
          bar={cpuPct}
        />
        <BigStat
          label="RAM"
          value={`${round1(memoryPct)}%`}
          sub={
            memoryTotal > 0
              ? `${formatBytes(memoryUsed)} / ${formatBytes(memoryTotal)}`
              : "—"
          }
          accent="sky"
          bar={memoryPct}
        />
        <BigStat
          label="Red"
          value={`${formatBytes(netRx)} ▼  ${formatBytes(netTx)} ▲`}
          sub="acumulado del satélite"
          accent="violet"
        />
      </div>

      <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
        <div className="mb-2 flex items-center justify-between">
          <h3 className="text-sm font-medium text-zinc-200">
            CPU% — últimos {MAX_POINTS} puntos
          </h3>
          <span className="text-xs text-zinc-500">
            {points.length} muestras
          </span>
        </div>
        <div className="h-56 w-full">
          {chartData.length === 0 ? (
            <div className="flex h-full items-center justify-center text-sm text-zinc-500">
              Esperando muestras del satélite...
            </div>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <LineChart
                data={chartData}
                margin={{ top: 8, right: 8, left: 0, bottom: 0 }}
              >
                <CartesianGrid stroke="#27272a" strokeDasharray="3 3" />
                <XAxis
                  dataKey="t"
                  stroke="#52525b"
                  fontSize={11}
                  tickLine={false}
                  axisLine={{ stroke: "#3f3f46" }}
                  minTickGap={32}
                />
                <YAxis
                  domain={[0, 100]}
                  stroke="#52525b"
                  fontSize={11}
                  tickLine={false}
                  axisLine={{ stroke: "#3f3f46" }}
                  tickFormatter={(v) => `${v}%`}
                  width={40}
                />
                <Tooltip
                  contentStyle={{
                    background: "#09090b",
                    border: "1px solid #27272a",
                    borderRadius: 8,
                    fontSize: 12,
                  }}
                  labelStyle={{ color: "#a1a1aa" }}
                  formatter={(value) => [`${typeof value === "number" ? value : Number(value) || 0}%`, "CPU"]}
                />
                <Line
                  type="monotone"
                  dataKey="cpu"
                  stroke="#10b981"
                  strokeWidth={2}
                  dot={false}
                  isAnimationActive={false}
                />
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>
      </div>
    </section>
  );
}

function BigStat({
  label,
  value,
  sub,
  accent,
  bar,
}: {
  label: string;
  value: string;
  sub?: string;
  accent: "emerald" | "sky" | "violet";
  bar?: number;
}) {
  const barColor = {
    emerald: "bg-emerald-500",
    sky: "bg-sky-500",
    violet: "bg-violet-500",
  }[accent];
  const pct = typeof bar === "number" ? Math.max(0, Math.min(100, bar)) : null;

  return (
    <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
      <div className="text-[10px] uppercase tracking-wider text-zinc-500">
        {label}
      </div>
      <div className="mt-2 text-3xl font-semibold text-zinc-100">{value}</div>
      {sub && <div className="mt-1 text-xs text-zinc-500">{sub}</div>}
      {pct !== null && (
        <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-zinc-800">
          <div
            className={`h-full ${barColor} transition-all duration-500`}
            style={{ width: `${pct}%` }}
          />
        </div>
      )}
    </div>
  );
}

function ConnectionBadge({
  phase,
  status,
}: {
  phase: ConnectionPhase;
  status: VmStatus;
}) {
  const map: Record<ConnectionPhase, { label: string; cls: string; dot: string }> = {
    idle: {
      label: "Esperando",
      cls: "border-zinc-700 bg-zinc-800/40 text-zinc-400",
      dot: "bg-zinc-500",
    },
    connecting: {
      label: "Conectando hub...",
      cls: "border-zinc-700 bg-zinc-800/40 text-zinc-300",
      dot: "bg-zinc-400 animate-pulse",
    },
    connected: {
      label: `Hub en vivo · VM ${status}`,
      cls: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
      dot: "bg-emerald-400",
    },
    reconnecting: {
      label: "Reconectando...",
      cls: "border-amber-500/40 bg-amber-500/10 text-amber-300",
      dot: "bg-amber-400 animate-pulse",
    },
    disconnected: {
      label: "Hub desconectado",
      cls: "border-rose-500/40 bg-rose-500/10 text-rose-300",
      dot: "bg-rose-400",
    },
    error: {
      label: "Error de hub",
      cls: "border-rose-500/40 bg-rose-500/10 text-rose-300",
      dot: "bg-rose-400",
    },
  };
  const v = map[phase];
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${v.cls}`}
    >
      <span className={`size-1.5 rounded-full ${v.dot}`} />
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

function formatTimeLabel(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const hh = String(d.getHours()).padStart(2, "0");
  const mm = String(d.getMinutes()).padStart(2, "0");
  const ss = String(d.getSeconds()).padStart(2, "0");
  return `${hh}:${mm}:${ss}`;
}
