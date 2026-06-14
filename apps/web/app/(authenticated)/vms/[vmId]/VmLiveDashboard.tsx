"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricsChart, type MetricsChartSeries } from "@/components/aethra/metrics-chart";
import { API_URL, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { VmMetricPoint, VmStatus } from "@/lib/types";

const MAX_POINTS = 60;

/**
 * Rangos del gráfico. "live" mantiene el stream SignalR (últimos {MAX_POINTS} puntos);
 * los demás llaman a GET /api/metrics/vms/{id}/history?hours=H (downsampled a 240 puntos).
 * El historial no trae disco (la query lo omite) → en histórico sólo se grafica CPU/RAM.
 */
const RANGES = [
  { key: "live", label: "Vivo", hours: 0, chartLabel: `últimos ${MAX_POINTS} puntos` },
  { key: "1h", label: "1 h", hours: 1, chartLabel: "última hora" },
  { key: "24h", label: "24 h", hours: 24, chartLabel: "últimas 24 h" },
  { key: "7d", label: "7 d", hours: 168, chartLabel: "últimos 7 días" },
] as const;

type RangeKey = (typeof RANGES)[number]["key"];

const LIVE_SERIES: MetricsChartSeries[] = [
  { dataKey: "cpu", label: "CPU", tone: "info" },
  { dataKey: "ram", label: "RAM", tone: "primary" },
  { dataKey: "disk", label: "Disco", tone: "warning" },
];

const HISTORY_SERIES: MetricsChartSeries[] = [
  { dataKey: "cpu", label: "CPU", tone: "info" },
  { dataKey: "ram", label: "RAM", tone: "primary" },
];

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

  // Rango del gráfico: "live" usa el stream SignalR; los demás cargan historial REST.
  const [range, setRange] = useState<RangeKey>("live");
  const [history, setHistory] = useState<VmMetricPoint[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

  // Reloj de cliente (1s) para el badge de frescura. Se inicia en useEffect → sin mismatch de hydration.
  const [nowMs, setNowMs] = useState<number | null>(null);

  useEffect(() => {
    setNowMs(Date.now());
    const id = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

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

  // Carga (y refresca cada 60s) el historial cuando el rango no es "vivo".
  useEffect(() => {
    if (range === "live") return;
    const cfg = RANGES.find((r) => r.key === range);
    if (!cfg) return;

    let cancelled = false;
    setHistoryLoading(true);
    setHistoryError(null);

    const load = () =>
      api<VmMetricPoint[]>(
        `/api/metrics/vms/${vmId}/history?hours=${cfg.hours}&points=240`,
      )
        .then((data) => {
          if (cancelled) return;
          setHistory(data);
          setHistoryLoading(false);
        })
        .catch(() => {
          if (cancelled) return;
          setHistoryError("No se pudo cargar el historial.");
          setHistoryLoading(false);
        });

    load();
    const id = setInterval(load, 60_000);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [range, vmId]);

  const latest = points.length > 0 ? points[points.length - 1] : null;
  // Antigüedad de la última muestra recibida (sólo relevante en "vivo").
  const lastSampleAgeSec =
    nowMs !== null && latest
      ? Math.max(0, Math.round((nowMs - Date.parse(latest.timestamp)) / 1000))
      : null;
  const memoryTotal = latest?.memoryTotalBytes ?? totalMemoryBytes ?? 0;
  const memoryUsed = latest?.memoryUsedBytes ?? 0;
  const memoryPct = memoryTotal > 0 ? (memoryUsed / memoryTotal) * 100 : 0;
  const diskTotal = latest?.diskTotalBytes ?? 0;
  const diskUsed = latest?.diskUsedBytes ?? 0;
  const diskFree = Math.max(0, diskTotal - diskUsed);
  const diskPct = diskTotal > 0 ? (diskUsed / diskTotal) * 100 : 0;
  const cpuPct = latest?.cpuPercent ?? 0;
  // Tasa de red (bytes/s): delta entre las dos últimas muestras del stream / delta de tiempo. Los
  // contadores del satélite son acumulativos; un reinicio baja el contador → se hace clamp a 0.
  const netRate = computeNetRate(points);

  // En "vivo" graficamos el stream; en histórico, la ventana cargada por REST.
  const displayPoints = range === "live" ? points : history;
  const activeRange = RANGES.find((r) => r.key === range) ?? RANGES[0];
  const chartSeries = range === "live" ? LIVE_SERIES : HISTORY_SERIES;

  const chartData = useMemo(
    () =>
      displayPoints.map((p) => ({
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
    [displayPoints],
  );

  // Exporta las muestras visibles (vivo o histórico) a CSV, 100% en el cliente (sin libs).
  const downloadCsv = () => {
    if (displayPoints.length === 0) return;
    const header =
      "timestamp,cpuPercent,memoryUsedBytes,memoryTotalBytes,diskUsedBytes,diskTotalBytes,netBytesReceived,netBytesSent";
    const rows = displayPoints.map((p) =>
      [
        p.timestamp,
        p.cpuPercent,
        p.memoryUsedBytes,
        p.memoryTotalBytes,
        p.diskUsedBytes,
        p.diskTotalBytes,
        p.netBytesReceived,
        p.netBytesSent,
      ].join(","),
    );
    const csv = [header, ...rows].join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `vms-${vmId}-${range}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  };

  return (
    <section className="flex flex-col gap-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Métricas en vivo
        </h2>
        <div className="flex items-center gap-2">
          {range === "live" ? <FreshnessBadge ageSec={lastSampleAgeSec} /> : null}
          <ConnectionBadge phase={phase} status={status} />
        </div>
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
          value={
            netRate
              ? `${formatBytes(netRate.rx)}/s ↓  ${formatBytes(netRate.tx)}/s ↑`
              : "—"
          }
          sub={netRate ? "tasa actual (rx / tx)" : "esperando muestras"}
        />
      </div>

      <Card>
        <CardHeader className="flex-col items-stretch gap-3 space-y-0 pb-2 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex flex-col gap-0.5">
            <CardTitle className="text-base">
              CPU · RAM{range === "live" ? " · Disco" : ""} (%)
            </CardTitle>
            <span className="text-xs text-muted-foreground">
              {activeRange.chartLabel}
              {historyLoading && range !== "live"
                ? " · cargando…"
                : ` · ${displayPoints.length} muestras`}
            </span>
          </div>
          <div className="flex items-center gap-2 self-start sm:self-auto">
            <button
              type="button"
              onClick={downloadCsv}
              disabled={displayPoints.length === 0}
              className="inline-flex items-center gap-1 rounded-md border border-border bg-muted/40 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground disabled:pointer-events-none disabled:opacity-50"
              title="Descargar las muestras visibles como CSV"
            >
              CSV ↓
            </button>
          <div
            className="inline-flex items-center gap-0.5 rounded-md border border-border bg-muted/40 p-0.5"
            role="group"
            aria-label="Rango del gráfico"
          >
            {RANGES.map((r) => (
              <button
                key={r.key}
                type="button"
                onClick={() => setRange(r.key)}
                aria-pressed={range === r.key}
                className={cn(
                  "rounded px-2.5 py-1 text-xs font-medium transition-colors",
                  range === r.key
                    ? "bg-background text-foreground shadow-sm"
                    : "text-muted-foreground hover:text-foreground",
                )}
              >
                {r.label}
              </button>
            ))}
          </div>
          </div>
        </CardHeader>
        <CardContent>
          {range !== "live" && historyError ? (
            <div className="flex h-56 items-center justify-center text-sm text-destructive">
              {historyError}
            </div>
          ) : range !== "live" && historyLoading && displayPoints.length === 0 ? (
            <div className="flex h-56 items-center justify-center text-sm text-muted-foreground">
              Cargando historial…
            </div>
          ) : chartData.length === 0 ? (
            <div className="flex h-56 items-center justify-center text-sm text-muted-foreground">
              {range === "live"
                ? "Esperando muestras del satélite…"
                : "Sin datos en este rango."}
            </div>
          ) : (
            <MetricsChart
              data={chartData}
              series={chartSeries}
              variant="line"
              formatValue={(v) => `${v}%`}
              formatX={range === "7d" ? formatDayTime : undefined}
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

function FreshnessBadge({ ageSec }: { ageSec: number | null }) {
  if (ageSec === null) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full border border-border bg-muted px-2.5 py-0.5 text-[11px] font-medium text-muted-foreground">
        <span className="size-1.5 rounded-full bg-muted-foreground" />
        sin muestras
      </span>
    );
  }
  const stale = ageSec > 30;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium",
        stale
          ? "border-warning/30 bg-warning/10 text-warning-foreground"
          : "border-success/30 bg-success/10 text-success-foreground",
      )}
      title="Tiempo desde la última muestra recibida del satélite"
    >
      <span className={cn("size-1.5 rounded-full", stale ? "bg-warning" : "bg-success")} />
      {stale ? "sin datos hace " : "hace "}
      {formatAge(ageSec)}
    </span>
  );
}

function formatAge(sec: number): string {
  if (sec < 60) return `${sec}s`;
  if (sec < 3600) return `${Math.floor(sec / 60)}m ${sec % 60}s`;
  return `${Math.floor(sec / 3600)}h ${Math.floor((sec % 3600) / 60)}m`;
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

/** Tasa de red en bytes/s entre las dos últimas muestras del stream (contadores acumulativos). */
function computeNetRate(
  points: VmMetricPoint[],
): { rx: number; tx: number } | null {
  if (points.length < 2) return null;
  const a = points[points.length - 2];
  const b = points[points.length - 1];
  const dt = (Date.parse(b.timestamp) - Date.parse(a.timestamp)) / 1000;
  if (!(dt > 0)) return null;
  return {
    rx: Math.max(0, b.netBytesReceived - a.netBytesReceived) / dt,
    tx: Math.max(0, b.netBytesSent - a.netBytesSent) / dt,
  };
}

/** Eje X para el rango de 7 días: incluye día/mes además de la hora. */
function formatDayTime(v: string | number): string {
  try {
    const d = new Date(v);
    return d.toLocaleString("es-ES", {
      day: "2-digit",
      month: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    });
  } catch {
    return String(v);
  }
}
