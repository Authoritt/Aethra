import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Boxes, Plus } from "lucide-react";
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
import type { ManagedServiceSummaryDto } from "@/lib/types";
import { ServiceStatusPill } from "./ServiceStatusPill";

export const dynamic = "force-dynamic";

async function fetchServices(): Promise<
  ManagedServiceSummaryDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/services`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ManagedServiceSummaryDto[];
}

export default async function ServicesPage() {
  const data = await fetchServices();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const services = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Servicios compartidos"
        description="Postgres, Redis, RabbitMQ y otros backends provisionados desde plantillas. Consumidos vía bindings desde applications."
        actions={
          <Button asChild>
            <Link href="/services/new">
              <Plus className="mr-2 h-4 w-4" />
              Crear servicio
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado. Verificá que la API esté corriendo.
          </CardContent>
        </Card>
      ) : services.length === 0 ? (
        <EmptyState
          icon={<Boxes className="h-6 w-6" />}
          title="Aún sin servicios"
          description="Crea un Postgres, Redis o RabbitMQ desde plantilla para que tus applications puedan consumirlo vía bindings."
          action={
            <Button asChild>
              <Link href="/services/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear servicio
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Slug</TableHead>
                <TableHead>Tipo</TableHead>
                <TableHead>Versión</TableHead>
                <TableHead>VM target</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Bindings</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {services.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>
                    <Link
                      href={`/services/${s.id}`}
                      className="flex flex-col"
                    >
                      <span className="font-mono text-sm text-foreground">
                        {s.slug}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {s.name}
                      </span>
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" className="font-mono text-[10px] uppercase">
                      {s.type}
                    </Badge>
                  </TableCell>
                  <TableCell className="font-mono text-xs">{s.version}</TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {s.target_vm_id.slice(0, 8)}
                  </TableCell>
                  <TableCell>
                    <ServiceStatusPill status={s.status} />
                  </TableCell>
                  <TableCell className="text-right">
                    <Badge variant="outline" className="font-mono text-xs">
                      {s.bindings_count}
                    </Badge>
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
