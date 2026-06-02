import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { ExternalLink, Lock, Unlock } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { CertStatusPill } from "@/components/aethra/cert-status-pill";
import { API_URL } from "@/lib/api";
import type { RouteDto } from "@/lib/types";
import { DeleteRouteButton } from "../delete-route-button";

export const dynamic = "force-dynamic";

async function fetchRoute(
  routeId: string,
): Promise<RouteDto | "unauthorized" | "notfound" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/proxy/routes/${routeId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as RouteDto;
}

export default async function RouteDetailPage({
  params,
}: {
  params: Promise<{ routeId: string }>;
}) {
  const { routeId } = await params;
  const data = await fetchRoute(routeId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando la ruta.
          </CardContent>
        </Card>
      </div>
    );
  }

  const route = data;
  const scheme = route.tls_enabled ? "https" : "http";
  const url = `${scheme}://${route.hostname}`;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Rutas", href: "/routes" },
          { label: route.hostname },
        ]}
        title={route.hostname}
        description={
          <>
            <span className="font-mono text-xs">{route.backend_url}</span>
          </>
        }
        actions={
          <>
            <Button asChild variant="outline">
              <a href={url} target="_blank" rel="noreferrer noopener">
                <ExternalLink className="mr-2 h-4 w-4" />
                Abrir
              </a>
            </Button>
            <DeleteRouteButton id={route.id} hostname={route.hostname} />
          </>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        {route.tls_enabled ? (
          <Badge variant="success">
            <Lock className="mr-1 h-3 w-3" />
            TLS habilitado
          </Badge>
        ) : (
          <Badge variant="outline">
            <Unlock className="mr-1 h-3 w-3" />
            HTTP
          </Badge>
        )}
        <CertStatusPill
          status={route.tls_enabled ? route.cert_status : "none"}
        />
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Overview
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv label="Hostname público" value={route.hostname} mono />
              <Kv label="Backend interno" value={route.backend_url} mono />
              <Kv label="Esquema" value={scheme.toUpperCase()} />
              <Kv label="Creado" value={formatDate(route.created_at)} />
              <Kv
                label="Actualizado"
                value={formatDate(route.updated_at)}
              />
            </dl>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Certificado TLS
            </CardTitle>
          </CardHeader>
          <CardContent>
            {route.tls_enabled ? (
              <dl className="flex flex-col gap-3 text-sm">
                <Kv label="Status" value={route.cert_status} />
                <Kv
                  label="Expira"
                  value={
                    route.cert_expires_at
                      ? formatDate(route.cert_expires_at)
                      : "—"
                  }
                />
                {route.cert_status === "failed" ? (
                  <p className="text-xs text-destructive">
                    El issuer no pudo emitir el certificado. Verificá DNS y
                    firewall (HTTP-01 challenge).
                  </p>
                ) : null}
              </dl>
            ) : (
              <p className="text-sm text-muted-foreground">
                TLS deshabilitado para esta ruta. El tráfico se sirve en HTTP
                plano.
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function Kv({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd
        className={`mt-0.5 break-all text-foreground ${mono ? "font-mono text-xs" : "text-sm"}`}
      >
        {value}
      </dd>
    </div>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
