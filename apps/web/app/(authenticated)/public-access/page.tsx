import Link from "next/link";
import { redirect } from "next/navigation";
import { ExternalLink, Network, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PublicAccessReconcileActions } from "@/components/aethra/PublicAccessReconcileActions";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { AppOverviewDto, PublicEndpointOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface PublicAccessFilters {
  q?: string;
  health?: string;
  appId?: string;
  environment?: string;
  dns?: string;
  tunnel?: string;
  monitor?: string;
}

export default async function PublicAccessPage({
  searchParams,
}: {
  searchParams: Promise<PublicAccessFilters>;
}) {
  const filters = await searchParams;
  const query = buildQuery(filters);
  const [data, appsData] = await Promise.all([
    serverFetch<PublicEndpointOverviewDto[]>(`/api/ops/public-endpoints${query}`),
    serverFetch<AppOverviewDto[]>("/api/ops/apps"),
  ]);
  if (data === "unauthorized" || appsData === "unauthorized") {
    redirect("/login");
  }
  const endpoints = Array.isArray(data) ? data : [];
  const apps = Array.isArray(appsData) ? appsData : [];
  const environmentOptions = Array.from(
    new Set([
      "dev",
      "staging",
      "production",
      ...endpoints
        .map((endpoint) => endpoint.environment)
        .filter((environment): environment is string => Boolean(environment)),
    ]),
  ).sort((a, b) => a.localeCompare(b));
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Public Access"
        description="Hosts publicos agrupados por owner operacional, rutas tecnicas, monitor y salud."
        actions={
          <Button asChild variant="outline">
            <Link href="/routes">Routes tecnicas</Link>
          </Button>
        }
      />

      <Card>
        <CardContent className="p-4">
          <form method="get" className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="q">Buscar</Label>
              <Input
                id="q"
                name="q"
                defaultValue={filters.q ?? ""}
                placeholder="host, app, backend, issue"
                className="w-64"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="health">Health</Label>
              <select
                id="health"
                name="health"
                defaultValue={filters.health ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                <option value="healthy">Healthy</option>
                <option value="degraded">Degraded</option>
                <option value="broken">Broken</option>
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="dns">DNS</Label>
              <select
                id="dns"
                name="dns"
                defaultValue={filters.dns ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                <option value="ok">OK</option>
                <option value="missing">Missing</option>
                <option value="wrong">Wrong target</option>
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="tunnel">Tunnel</Label>
              <select
                id="tunnel"
                name="tunnel"
                defaultValue={filters.tunnel ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                <option value="ok">OK</option>
                <option value="missing">Missing</option>
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="monitor">Monitor</Label>
              <select
                id="monitor"
                name="monitor"
                defaultValue={filters.monitor ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                <option value="up">Up</option>
                <option value="down">Down</option>
                <option value="missing">Missing</option>
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="appId">App</Label>
              <select
                id="appId"
                name="appId"
                defaultValue={filters.appId ?? ""}
                className="flex h-10 max-w-64 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todas</option>
                {apps.map((app) => (
                  <option key={app.id} value={app.id}>
                    {app.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="environment">Environment</Label>
              <select
                id="environment"
                name="environment"
                defaultValue={filters.environment ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                {environmentOptions.map((env) => (
                  <option key={env} value={env}>
                    {env}
                  </option>
                ))}
              </select>
            </div>
            <Button type="submit">Filtrar</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/public-access">
                  <X className="mr-2 h-4 w-4" />
                  Limpiar
                </Link>
              </Button>
            ) : null}
          </form>
        </CardContent>
      </Card>

      {data === "error" || data === "notfound" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar Public Access.
          </CardContent>
        </Card>
      ) : endpoints.length === 0 ? (
        <EmptyState
          icon={<Network className="h-6 w-6" />}
          title="Sin endpoints publicos"
          description={
            hasFilters
              ? "No hay endpoints que coincidan con los filtros actuales."
              : "Cuando existan routes o dominios de apps apareceran agrupados por hostname."
          }
        />
      ) : (
        <div className="space-y-4">
          {endpoints.map((endpoint) => (
            <Card key={endpoint.hostname}>
              <CardHeader className="space-y-3">
                <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                  <div className="min-w-0">
                    <CardTitle className="flex min-w-0 items-center gap-2 text-base">
                      <Link
                        href={endpoint.url}
                        target="_blank"
                        className="truncate hover:text-primary"
                      >
                        {endpoint.hostname}
                      </Link>
                      <ExternalLink className="h-4 w-4 shrink-0 text-muted-foreground" />
                    </CardTitle>
                    <p className="mt-1 truncate text-sm text-muted-foreground">
                      {endpoint.appName
                        ? `${endpoint.appName} / ${endpoint.tenantName ?? "default"} / ${endpoint.environment ?? "-"}`
                        : "Sin owner operacional"}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <StatusBadge status={endpoint.healthStatus} />
                    <Badge variant="outline">{endpoint.ownerStatus}</Badge>
                    <Badge variant={endpoint.dnsConfigured ? "success" : "warning"} className="font-mono text-[10px] uppercase">
                      DNS {endpoint.dnsConfigured ? "ok" : "pending"}
                    </Badge>
                    <Badge variant={endpoint.tunnelConfigured ? "success" : "warning"} className="font-mono text-[10px] uppercase">
                      Tunnel {endpoint.tunnelConfigured ? "ok" : "pending"}
                    </Badge>
                    <Badge variant={endpoint.tlsEnabled ? "success" : "warning"} className="font-mono text-[10px] uppercase">
                      TLS {endpoint.tlsEnabled ? "ok" : "pending"}
                    </Badge>
                    {endpoint.monitorStatus ? <Badge variant="outline">monitor {endpoint.monitorStatus}</Badge> : null}
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-2 rounded-md border bg-muted/20 p-3 text-xs md:grid-cols-3">
                  <SmallStat label="DNS target" value={endpoint.dnsTarget ?? "-"} mono />
                  <SmallStat label="Expected target" value={endpoint.expectedDnsTarget ?? "-"} mono />
                  <SmallStat label="Tunnel" value={endpoint.tunnelName ?? "-"} />
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  {endpoint.appEnvironmentId ? (
                    <>
                      <Button asChild size="sm" variant="outline">
                        <Link href={`/instances/${endpoint.appEnvironmentId}`}>
                          App Environment
                        </Link>
                      </Button>
                      <PublicAccessReconcileActions appEnvironmentId={endpoint.appEnvironmentId} />
                    </>
                  ) : (
                    <Badge variant="warning" className="font-mono text-[10px]">
                      owner_missing
                    </Badge>
                  )}
                </div>
              </CardHeader>
              <CardContent>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Path</TableHead>
                      <TableHead>Backend</TableHead>
                      <TableHead>Route</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {endpoint.routes.map((route) => (
                      <TableRow key={route.routeId}>
                        <TableCell className="font-mono text-xs">{route.pathPrefix}</TableCell>
                        <TableCell className="font-mono text-xs">{route.backendUrl}</TableCell>
                        <TableCell>
                          <Link href={`/routes/${route.routeId}`} className="text-sm text-primary">
                            Ver route
                          </Link>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                {endpoint.issues.length > 0 ? (
                  <div className="mt-3 flex flex-wrap gap-2">
                    {endpoint.issues.map((issue) => (
                      <Badge key={issue} variant="warning" className="font-mono text-[10px]">
                        {issue}
                      </Badge>
                    ))}
                  </div>
                ) : null}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const variant = status === "healthy" ? "success" : status === "broken" ? "destructive" : "warning";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function SmallStat({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="min-w-0">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className={`mt-0.5 truncate ${mono ? "font-mono" : ""}`} title={value}>
        {value}
      </p>
    </div>
  );
}

function buildQuery(filters: PublicAccessFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
