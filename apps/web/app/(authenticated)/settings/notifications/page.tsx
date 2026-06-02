import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Bell, Plus, History } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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
import type { NotificationChannelDto } from "@/lib/types";
import { TestChannelButton } from "./TestChannelButton";
import { DeleteChannelButton } from "./DeleteChannelButton";
import { ToggleActiveSwitch } from "./ToggleActiveSwitch";

export const dynamic = "force-dynamic";

async function fetchChannels(): Promise<
  NotificationChannelDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/notifications/channels/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as NotificationChannelDto[];
}

export default async function NotificationsPage() {
  const data = await fetchChannels();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const channels = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Notificaciones" },
        ]}
        title="Notificaciones"
        description="Canales que reciben alertas operativas (monitores down, builds fallidos, certificados expirados). El payload se manda al webhook configurado segun cada tipo."
        actions={
          <div className="flex items-center gap-2">
            <Button asChild variant="outline">
              <Link href="/settings/notifications/deliveries">
                <History className="mr-2 h-4 w-4" />
                Historial
              </Link>
            </Button>
            <Button asChild>
              <Link href="/settings/notifications/new">
                <Plus className="mr-2 h-4 w-4" />
                Nuevo canal
              </Link>
            </Button>
          </div>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado.
          </CardContent>
        </Card>
      ) : channels.length === 0 ? (
        <EmptyState
          icon={<Bell className="h-6 w-6" />}
          title="Sin canales configurados"
          description="Crea un canal Slack/Discord/Telegram/Email/Webhook para recibir alertas de monitores, builds y deploys."
          action={
            <Button asChild>
              <Link href="/settings/notifications/new">
                <Plus className="mr-2 h-4 w-4" />
                Nuevo canal
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Tipo</TableHead>
                <TableHead>Eventos</TableHead>
                <TableHead>Activo</TableHead>
                <TableHead>Ultimo envio</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {channels.map((c) => (
                <TableRow key={c.id}>
                  <TableCell className="align-top">
                    <div className="font-medium">{c.name}</div>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {c.id}
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className="font-mono text-[10px] uppercase">
                      {c.type}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    {c.eventFilters.length === 0 ? (
                      <Badge variant="outline">todos</Badge>
                    ) : (
                      <div className="flex flex-wrap gap-1">
                        {c.eventFilters.map((e) => (
                          <Badge key={e} variant="outline" className="font-mono text-[10px]">
                            {e}
                          </Badge>
                        ))}
                      </div>
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    <ToggleActiveSwitch id={c.id} isActive={c.isActive} />
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.lastDeliveredAt)}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      <TestChannelButton id={c.id} name={c.name} />
                      <DeleteChannelButton id={c.id} name={c.name} />
                    </div>
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

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "hace unos seg.";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  return `hace ${days} d`;
}
