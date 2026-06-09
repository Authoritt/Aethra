import Link from "next/link";
import { redirect } from "next/navigation";
import { AlertTriangle, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
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
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { AppOverviewDto, OperationalIssueDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface IssueFilters {
  q?: string;
  severity?: string;
  resourceType?: string;
  appId?: string;
}

export default async function OperationalIssuesPage({
  searchParams,
}: {
  searchParams: Promise<IssueFilters>;
}) {
  const filters = await searchParams;
  const query = buildQuery(filters);
  const [data, appsData] = await Promise.all([
    serverFetch<OperationalIssueDto[]>(`/api/ops/operational-issues${query}`),
    serverFetch<AppOverviewDto[]>("/api/ops/apps"),
  ]);
  if (data === "unauthorized" || appsData === "unauthorized") {
    redirect("/login");
  }
  const issues = Array.isArray(data) ? data : [];
  const apps = Array.isArray(appsData) ? appsData : [];
  const resourceTypes = Array.from(new Set(["AppEnvironment", "Release", "PublicEndpoint", ...issues.map((i) => i.resourceType)]));
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Operational Issues"
        description="Inbox accionable de problemas derivados de app environments, releases, machines y public access."
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
                placeholder="codigo, app, tenant, recurso"
                className="w-64"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="severity">Severity</Label>
              <select
                id="severity"
                name="severity"
                defaultValue={filters.severity ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todas</option>
                <option value="critical">Critical</option>
                <option value="warning">Warning</option>
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="resourceType">Resource</Label>
              <select
                id="resourceType"
                name="resourceType"
                defaultValue={filters.resourceType ?? ""}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">Todos</option>
                {resourceTypes.map((type) => (
                  <option key={type} value={type}>
                    {type}
                  </option>
                ))}
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
            <Button type="submit">Filtrar</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/operational-issues">
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
            No se pudo cargar issues operativos.
          </CardContent>
        </Card>
      ) : issues.length === 0 ? (
        <EmptyState
          icon={<AlertTriangle className="h-6 w-6" />}
          title="No hay issues operativos"
          description={
            hasFilters
              ? "No hay issues que coincidan con los filtros actuales."
              : "Los fallos derivados apareceran aqui con owner y recurso."
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Severity</TableHead>
                <TableHead>Issue</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Tenant</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead>Resource</TableHead>
                <TableHead className="text-right">Action</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {issues.map((issue) => (
                <TableRow key={issue.id}>
                  <TableCell><SeverityBadge severity={issue.severity} /></TableCell>
                  <TableCell>
                    <div className="font-medium">{issue.title}</div>
                    <div className="font-mono text-[11px] text-muted-foreground">{issue.code}</div>
                  </TableCell>
                  <TableCell>{issue.appName ?? "-"}</TableCell>
                  <TableCell>{issue.tenantName ?? "-"}</TableCell>
                  <TableCell className="font-mono text-xs">{issue.environment ?? "-"}</TableCell>
                  <TableCell>
                    {issue.suggestedHref ? (
                      <Link href={issue.suggestedHref} className="text-primary">
                        {issue.resourceType}
                      </Link>
                    ) : (
                      <span>{issue.resourceType}</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    {issue.suggestedHref ? (
                      <Button asChild size="sm" variant="outline">
                        <Link href={issue.suggestedHref}>
                          {issue.suggestedAction}
                        </Link>
                      </Button>
                    ) : (
                      <span className="text-sm text-muted-foreground">
                        {issue.suggestedAction}
                      </span>
                    )}
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

function SeverityBadge({ severity }: { severity: string }) {
  const variant = severity === "critical" ? "destructive" : severity === "warning" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{severity}</Badge>;
}

function buildQuery(filters: IssueFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
