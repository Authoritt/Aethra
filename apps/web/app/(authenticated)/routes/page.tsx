import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Network, Plus } from "lucide-react";
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
import { CertStatusPill } from "@/components/aethra/cert-status-pill";
import { API_URL } from "@/lib/api";
import type { RouteDto } from "@/lib/types";
import { DeleteRouteButton } from "./delete-route-button";

export const dynamic = "force-dynamic";

async function fetchRoutes(): Promise<RouteDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/proxy/routes`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as RouteDto[];
}

export default async function RoutesPage() {
  const data = await fetchRoutes();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const routes = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Rutas"
        description="Reverse proxy YARP con terminación TLS por hostname."
        actions={
          <Button asChild>
            <Link href="/routes/new">
              <Plus className="mr-2 h-4 w-4" />
              Nueva ruta
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
      ) : routes.length === 0 ? (
        <EmptyState
          icon={<Network className="h-6 w-6" />}
          title="Aún sin rutas"
          description="Creá tu primera ruta para exponer un backend con TLS automático."
          action={
            <Button asChild>
              <Link href="/routes/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear ruta
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Hostname</TableHead>
                <TableHead>Backend</TableHead>
                <TableHead>TLS</TableHead>
                <TableHead>Expira</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {routes.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <Link
                      href={`/routes/${r.id}`}
                      className="font-medium text-foreground hover:text-primary"
                    >
                      {r.hostname}
                    </Link>
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {r.backend_url}
                  </TableCell>
                  <TableCell>
                    <CertStatusPill
                      status={r.tls_enabled ? r.cert_status : "none"}
                    />
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatExpires(r.cert_expires_at)}
                  </TableCell>
                  <TableCell className="text-right">
                    <DeleteRouteButton id={r.id} hostname={r.hostname} />
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

function formatExpires(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}
