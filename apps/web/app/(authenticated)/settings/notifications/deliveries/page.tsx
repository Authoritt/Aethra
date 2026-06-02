import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type {
  NotificationDeliveryDto,
  NotificationDeliveryStatus,
} from "@/lib/types";
import { History } from "lucide-react";

export const dynamic = "force-dynamic";

async function fetchDeliveries(): Promise<
  NotificationDeliveryDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/notifications/deliveries/?limit=100`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as NotificationDeliveryDto[];
}

const STATUS_VARIANTS: Record<
  NotificationDeliveryStatus,
  "success" | "warning" | "outline"
> = {
  Sent: "success",
  Failed: "warning",
  Pending: "outline",
};

export default async function DeliveriesPage() {
  const data = await fetchDeliveries();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const rows = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Notificaciones", href: "/settings/notifications" },
          { label: "Historial" },
        ]}
        title="Historial de entregas"
        description="Ultimas 100 notificaciones enviadas o intentadas. Las Pending se reintentaran segun el backoff del dispatcher."
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el historial.
          </CardContent>
        </Card>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<History className="h-6 w-6" />}
          title="Sin entregas registradas"
          description="No hay notificaciones disparadas todavia. Activa monitores o builds para verlas aparecer."
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Canal</TableHead>
                <TableHead>Evento</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Intentos</TableHead>
                <TableHead>Creada</TableHead>
                <TableHead>Enviada</TableHead>
                <TableHead>Error</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((d) => (
                <TableRow key={d.id}>
                  <TableCell className="align-top">
                    <div>{d.channelName}</div>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {d.channelId}
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className="font-mono text-[10px]">
                      {d.eventType}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge
                      variant={STATUS_VARIANTS[d.status] ?? "outline"}
                      className="font-mono text-[10px]"
                    >
                      {d.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">{d.attempts}</TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(d.createdAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(d.sentAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-destructive">
                    {d.error
                      ? d.error.slice(0, 120)
                      : "—"}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
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
  });
}
