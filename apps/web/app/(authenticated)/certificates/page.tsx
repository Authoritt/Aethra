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
import type { CertificateDto } from "@/lib/types";
import { RenewCertificateButton, RequestCertificateForm } from "./CertificateActions";

export const dynamic = "force-dynamic";

export default async function CertificatesPage() {
  const data = await serverFetch<CertificateDto[]>("/api/proxy/certificates");
  if (data === "unauthorized") redirect("/login");
  const errored = data === "error" || data === "notfound";
  const certificates = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Certificates"
        description="Certificados TLS edge/origin gestionados por Aethra y usados por Public Access."
      />

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-base">Emitir certificado</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestCertificateForm />
        </CardContent>
      </Card>

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudieron cargar los certificados.
          </CardContent>
        </Card>
      ) : certificates.length === 0 ? (
        <EmptyState
          icon={<ShieldCheck className="h-6 w-6" />}
          title="Sin certificados"
          description="Cuando emitas o renueves certificados apareceran aqui."
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Hostname</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Expira</TableHead>
                <TableHead>Renovar desde</TableHead>
                <TableHead>Error</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {certificates.map((cert) => (
                <TableRow key={cert.id}>
                  <TableCell>
                    <div className="font-mono text-xs">{cert.hostname}</div>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">{cert.id}</div>
                  </TableCell>
                  <TableCell>
                    <Badge variant={cert.status === "issued" ? "success" : cert.status === "failed" ? "destructive" : "outline"}>
                      {cert.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">{formatDate(cert.notAfter)}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">{formatDate(cert.renewAfter)}</TableCell>
                  <TableCell className="max-w-xs truncate text-xs text-muted-foreground" title={cert.lastError ?? undefined}>
                    {cert.lastError ?? "-"}
                  </TableCell>
                  <TableCell className="text-right">
                    <RenewCertificateButton certificateId={cert.id} />
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

function formatDate(value: string | null) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}
