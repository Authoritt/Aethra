"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { API_URL, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  ContainerListSnapshot,
  ContainerStat,
  VmContainersDto,
} from "@/lib/types";

interface Props {
  vmId: string;
  initial: VmContainersDto;
}

/**
 * Panel de contenedores del detalle de VM. Lista TODOS los contenedores del host (gestionados por
 * Aethra o no) con stats de uso (CPU/mem/disco/red). Carga inicial vía REST (server-fetch) y se
 * actualiza en vivo por el hub `dashboard` (evento `VmContainersUpdated`, ~cada 15s). Mismo patrón
 * de conexión que VmLiveDashboard.
 */
export default function VmContainers({ vmId, initial }: Props) {
  const [containers, setContainers] = useState<ContainerStat[]>(
    initial.containers,
  );
  const [updatedAt, setUpdatedAt] = useState<string | null>(initial.timestamp);
  const [live, setLive] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let cancelled = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on(
      "VmContainersUpdated",
      (incomingVmId: string, snapshot: ContainerListSnapshot) => {
        if (incomingVmId !== vmId) return;
        setContainers(sortContainers(snapshot.containers ?? []));
        setUpdatedAt(snapshot.timestamp);
      },
    );

    connection.onreconnecting(() => setLive(false));
    connection.onreconnected(() => {
      setLive(true);
      connection.invoke("JoinVm", vmId).catch(() => {});
    });
    connection.onclose(() => setLive(false));

    connection
      .start()
      .then(async () => {
        if (cancelled) return;
        setLive(true);
        try {
          await connection.invoke("JoinVm", vmId);
        } catch {
          // hub sin método o sin permiso — no es fatal
        }
      })
      .catch(() => {
        if (!cancelled) setLive(false);
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

  const refresh = () =>
    api<VmContainersDto>(`/api/metrics/vms/${vmId}/containers`)
      .then((data) => {
        setContainers(sortContainers(data.containers ?? []));
        setUpdatedAt(data.timestamp);
      })
      .catch(() => {});

  const sorted = useMemo(() => sortContainers(containers), [containers]);
  const runningCount = sorted.filter((c) => isRunning(c.state)).length;

  return (
    <section className="mt-6 flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Contenedores
        </h2>
        <div className="flex items-center gap-2">
          <span className="text-xs text-muted-foreground">
            {sorted.length} en total · {runningCount} corriendo
          </span>
          <LiveBadge live={live} updatedAt={updatedAt} />
          <button
            type="button"
            onClick={refresh}
            className="inline-flex items-center gap-1 rounded-md border border-border bg-muted/40 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
            title="Recargar ahora"
          >
            Refrescar
          </button>
        </div>
      </div>

      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-base">Todos los contenedores del host</CardTitle>
          <span className="text-xs text-muted-foreground">
            Incluye los que no pertenecen a Aethra. CPU/memoria/red/disco para los que corren.
          </span>
        </CardHeader>
        <CardContent>
          {sorted.length === 0 ? (
            <div className="flex h-32 items-center justify-center text-sm text-muted-foreground">
              Sin datos de contenedores todavía (esperando al satélite…).
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Estado</TableHead>
                  <TableHead>Nombre</TableHead>
                  <TableHead>Imagen</TableHead>
                  <TableHead className="text-right">CPU</TableHead>
                  <TableHead className="text-right">Memoria</TableHead>
                  <TableHead className="text-right">Disco</TableHead>
                  <TableHead className="text-right">Red (↓/↑)</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sorted.map((c) => {
                  const running = isRunning(c.state);
                  const memPct =
                    c.memoryUsedBytes != null &&
                    c.memoryLimitBytes != null &&
                    c.memoryLimitBytes > 0
                      ? (c.memoryUsedBytes / c.memoryLimitBytes) * 100
                      : null;
                  const disk = c.sizeRootFsBytes ?? c.sizeRwBytes;
                  return (
                    <TableRow key={c.id} className={running ? "" : "opacity-60"}>
                      <TableCell>
                        <Badge variant={stateVariant(c.state)}>
                          {c.state || c.status || "—"}
                        </Badge>
                      </TableCell>
                      <TableCell
                        className="max-w-[16rem] truncate font-medium"
                        title={c.name}
                      >
                        {c.name || "—"}
                      </TableCell>
                      <TableCell
                        className="max-w-[18rem] truncate font-mono text-xs text-muted-foreground"
                        title={c.image}
                      >
                        {shortImage(c.image)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {c.cpuPercent != null ? `${round1(c.cpuPercent)}%` : "—"}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {c.memoryUsedBytes != null ? (
                          <span title={memPct != null ? `${round1(memPct)}%` : undefined}>
                            {formatBytes(c.memoryUsedBytes)}
                            {c.memoryLimitBytes != null && c.memoryLimitBytes > 0 ? (
                              <span className="text-muted-foreground">
                                {" "}
                                / {formatBytes(c.memoryLimitBytes)}
                              </span>
                            ) : null}
                          </span>
                        ) : (
                          "—"
                        )}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {disk != null ? formatBytes(disk) : "—"}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-xs">
                        {c.netRxBytes != null && c.netTxBytes != null
                          ? `${formatBytes(c.netRxBytes)} / ${formatBytes(c.netTxBytes)}`
                          : "—"}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function LiveBadge({
  live,
  updatedAt,
}: {
  live: boolean;
  updatedAt: string | null;
}) {
  const label = live
    ? "En vivo"
    : updatedAt
      ? "Hub desconectado"
      : "Esperando";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium",
        live
          ? "border-success/30 bg-success/10 text-success-foreground"
          : "border-border bg-muted text-muted-foreground",
      )}
      title={updatedAt ? `Último snapshot: ${new Date(updatedAt).toLocaleString()}` : undefined}
    >
      <span
        className={cn(
          "size-1.5 rounded-full",
          live ? "bg-success" : "bg-muted-foreground",
        )}
      />
      {label}
    </span>
  );
}

function isRunning(state: string): boolean {
  return state?.toLowerCase() === "running";
}

function stateVariant(
  state: string,
): "success" | "secondary" | "destructive" | "warning" {
  switch (state?.toLowerCase()) {
    case "running":
      return "success";
    case "paused":
      return "warning";
    case "exited":
    case "dead":
      return "destructive";
    default:
      return "secondary";
  }
}

function sortContainers(list: ContainerStat[]): ContainerStat[] {
  return [...list].sort((a, b) => {
    const ar = isRunning(a.state);
    const br = isRunning(b.state);
    if (ar !== br) return ar ? -1 : 1;
    return (a.name || "").localeCompare(b.name || "");
  });
}

/** Quita el digest/registry largo para que la imagen quepa: `repo:tag`. */
function shortImage(image: string): string {
  if (!image) return "—";
  const atIdx = image.indexOf("@");
  const clean = atIdx > 0 ? image.slice(0, atIdx) : image;
  const slash = clean.lastIndexOf("/");
  return slash > 0 ? clean.slice(slash + 1) : clean;
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
