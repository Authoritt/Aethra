"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Database, Loader2, Play, RotateCcw, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ApiError, api } from "@/lib/api";
import type { ServiceBackupDto, ServiceBackupStatus } from "@/lib/types";

const STATUS_VARIANTS: Record<
  ServiceBackupStatus,
  "success" | "warning" | "outline"
> = {
  Completed: "success",
  Failed: "warning",
  Running: "outline",
};

export function BackupsTab({ serviceId }: { serviceId: string }) {
  const router = useRouter();
  const [rows, setRows] = useState<ServiceBackupDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyRun, setBusyRun] = useState(false);
  const [confirmRestore, setConfirmRestore] = useState<ServiceBackupDto | null>(
    null,
  );
  const [busyRestore, setBusyRestore] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const data = await api<ServiceBackupDto[]>(
        `/api/services/${encodeURIComponent(serviceId)}/backups?limit=100`,
      );
      setRows(data);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serviceId]);

  async function onRun() {
    setBusyRun(true);
    try {
      await api(
        `/api/services/${encodeURIComponent(serviceId)}/backups/run`,
        { method: "POST" },
      );
      toast.success("Backup encolado");
      await load();
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusyRun(false);
    }
  }

  async function onConfirmRestore() {
    if (!confirmRestore) return;
    setBusyRestore(true);
    try {
      await api(
        `/api/services/${encodeURIComponent(serviceId)}/backups/${encodeURIComponent(confirmRestore.id)}/restore`,
        { method: "POST" },
      );
      toast.success("Restore aplicado");
      setConfirmRestore(null);
      await load();
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusyRestore(false);
    }
  }

  async function onDelete(id: string) {
    if (!confirm("Eliminar este backup?")) return;
    try {
      await api(
        `/api/services/${encodeURIComponent(serviceId)}/backups/${encodeURIComponent(id)}`,
        { method: "DELETE" },
      );
      toast.success("Backup eliminado");
      await load();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    }
  }

  return (
    <section className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Backups ({rows.length})
        </h2>
        <Button size="sm" onClick={onRun} disabled={busyRun}>
          {busyRun ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <Play className="mr-2 h-4 w-4" />
          )}
          Backup ahora
        </Button>
      </div>

      {loading ? (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            Cargando...
          </CardContent>
        </Card>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<Database className="h-6 w-6" />}
          title="Sin backups"
          description="Ejecuta un backup manual o configura una BackupPolicy para que se disparen automaticamente segun el cron."
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Status</TableHead>
                <TableHead>Inicio</TableHead>
                <TableHead>Fin</TableHead>
                <TableHead>Tamaño</TableHead>
                <TableHead>Destino</TableHead>
                <TableHead>Error</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((b) => (
                <TableRow key={b.id}>
                  <TableCell className="align-top">
                    <Badge
                      variant={STATUS_VARIANTS[b.status] ?? "outline"}
                      className="font-mono text-[10px]"
                    >
                      {b.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top text-xs">
                    {formatDate(b.startedAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs">
                    {formatDate(b.finishedAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs font-mono">
                    {b.sizeBytes ? formatBytes(b.sizeBytes) : "—"}
                  </TableCell>
                  <TableCell className="align-top text-xs font-mono">
                    {truncate(b.destinationPath, 50)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-destructive">
                    {b.errorMessage ? truncate(b.errorMessage, 80) : "—"}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      {b.status === "Completed" ? (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setConfirmRestore(b)}
                        >
                          <RotateCcw className="mr-2 h-4 w-4" />
                          Restore
                        </Button>
                      ) : null}
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => onDelete(b.id)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <Dialog
        open={confirmRestore !== null}
        onOpenChange={(o) => !o && setConfirmRestore(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirmar restore</DialogTitle>
            <DialogDescription>
              Vas a restaurar el backup{" "}
              <span className="font-mono">{confirmRestore?.id}</span>. Esta
              accion puede sobreescribir datos actuales del servicio.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmRestore(null)}>
              Cancelar
            </Button>
            <Button onClick={onConfirmRestore} disabled={busyRestore}>
              {busyRestore ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Restaurar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function truncate(s: string, max: number): string {
  return s.length <= max ? s : s.slice(0, max) + "...";
}
