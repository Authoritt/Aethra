import Link from "next/link";
import { redirect } from "next/navigation";
import { AlertTriangle } from "lucide-react";
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
import { serverFetch } from "@/lib/server-fetch";
import type { OperationalIssueDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function OperationalIssuesPage() {
  const data = await serverFetch<OperationalIssueDto[]>("/api/ops/operational-issues");
  if (data === "unauthorized") redirect("/login");
  const issues = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Operational Issues"
        description="Problemas derivados de app environments, releases, machines y public access."
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar issues operativos.
          </CardContent>
        </Card>
      ) : issues.length === 0 ? (
        <EmptyState
          icon={<AlertTriangle className="h-6 w-6" />}
          title="No hay issues operativos"
          description="Los fallos derivados aparecerán aquí con owner y recurso."
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
                    {issue.appEnvironmentId ? (
                      <Link href={`/instances/${issue.appEnvironmentId}`} className="text-primary">
                        {issue.resourceType}
                      </Link>
                    ) : (
                      <span>{issue.resourceType}</span>
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
