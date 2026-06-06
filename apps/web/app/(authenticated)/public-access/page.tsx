import Link from "next/link";
import { redirect } from "next/navigation";
import { ExternalLink, Network } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
import { serverFetch } from "@/lib/server-fetch";
import type { PublicEndpointOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function PublicAccessPage() {
  const data = await serverFetch<PublicEndpointOverviewDto[]>("/api/ops/public-endpoints");
  if (data === "unauthorized") redirect("/login");
  const endpoints = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Public Access"
        description="Hosts públicos agrupados por owner operacional, rutas técnicas, monitor y salud."
        actions={
          <Button asChild variant="outline">
            <Link href="/routes">Routes técnicas</Link>
          </Button>
        }
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar Public Access.
          </CardContent>
        </Card>
      ) : endpoints.length === 0 ? (
        <EmptyState
          icon={<Network className="h-6 w-6" />}
          title="Sin endpoints públicos"
          description="Cuando existan routes o dominios de apps aparecerán agrupados por hostname."
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
                    {endpoint.monitorStatus ? <Badge variant="outline">monitor {endpoint.monitorStatus}</Badge> : null}
                  </div>
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
