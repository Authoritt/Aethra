import { redirect } from "next/navigation";
import { ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { OperationalPoliciesDto, OperationalThresholdDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function PoliciesPage() {
  const data = await serverFetch<OperationalPoliciesDto>("/api/ops/policies");
  if (data === "unauthorized") redirect("/login");
  const policies = data !== "error" && data !== "notfound" ? data : null;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Policies"
        description="Catalogo efectivo de reglas operacionales que gobiernan deploys, Public Access y readiness."
      />

      {!policies ? (
        <EmptyState
          icon={<ShieldCheck className="h-6 w-6" />}
          title="Policies no disponibles"
          description="No se pudo cargar el catalogo operacional."
        />
      ) : (
        <div className="grid gap-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Public Access por ambiente</CardTitle>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Environment</TableHead>
                    <TableHead>Reconcile</TableHead>
                    <TableHead>Edge TLS</TableHead>
                    <TableHead>Monitor</TableHead>
                    <TableHead>Descripcion</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {policies.publicAccess.map((policy) => (
                    <TableRow key={policy.environment}>
                      <TableCell className="font-mono text-xs">{policy.environment}</TableCell>
                      <TableCell><Badge variant="outline">{policy.reconciliationPolicy}</Badge></TableCell>
                      <TableCell><Badge variant="outline">{policy.edgeTlsPolicy}</Badge></TableCell>
                      <TableCell>{policy.monitorRequired ? "required" : "optional"}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">{policy.description}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <ThresholdCard title="Machine readiness" rows={policies.machineReadiness} />
          <ThresholdCard title="Release actions" rows={policies.release} />
        </div>
      )}
    </div>
  );
}

function ThresholdCard({
  title,
  rows,
}: {
  title: string;
  rows: OperationalThresholdDto[];
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Key</TableHead>
              <TableHead>Value</TableHead>
              <TableHead>Description</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.key}>
                <TableCell className="font-mono text-xs">{row.key}</TableCell>
                <TableCell><Badge variant="outline">{row.value}</Badge></TableCell>
                <TableCell className="text-sm text-muted-foreground">{row.description}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
