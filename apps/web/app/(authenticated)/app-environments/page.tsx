import Link from "next/link";
import { redirect } from "next/navigation";
import { ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
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
import type { AppEnvironmentOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function AppEnvironmentsPage() {
  const data = await serverFetch<AppEnvironmentOverviewDto[]>("/api/ops/app-environments");
  if (data === "unauthorized") redirect("/login");
  const envs = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="App Environments"
        description="La unidad operativa real: app, tenant, ambiente, máquina, release y URL pública."
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar la vista operacional.
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>App Environment</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Tenant</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Machine</TableHead>
                <TableHead>Release</TableHead>
                <TableHead>URL</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {envs.map((env) => (
                <TableRow key={env.id}>
                  <TableCell>
                    <Link href={`/instances/${env.id}`} className="font-medium hover:text-primary">
                      {env.slug}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Link href={`/apps/${env.appId}`} className="hover:text-primary">
                      {env.appName}
                    </Link>
                  </TableCell>
                  <TableCell>{env.tenantName}</TableCell>
                  <TableCell className="font-mono text-xs">{env.environment}</TableCell>
                  <TableCell>
                    <StatusBadge status={env.healthStatus} />
                  </TableCell>
                  <TableCell>
                    <Link href={`/vms/${env.machineId}`} className="hover:text-primary">
                      {env.machineName}
                    </Link>
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {env.latestReleaseStatus ?? "-"}
                  </TableCell>
                  <TableCell className="max-w-xs">
                    {env.publicUrl ? (
                      <Link
                        href={env.publicUrl}
                        target="_blank"
                        className="inline-flex max-w-full items-center gap-1 truncate text-primary"
                      >
                        <ExternalLink className="h-3 w-3 shrink-0" />
                        <span className="truncate">{env.publicUrl.replace(/^https?:\/\//, "")}</span>
                      </Link>
                    ) : (
                      <span className="text-muted-foreground">-</span>
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

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant = normalized === "healthy" ? "success" : normalized === "failed" ? "destructive" : normalized === "deploying" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}
