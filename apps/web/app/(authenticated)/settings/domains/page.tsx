import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Globe, Plus } from "lucide-react";
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
import type { BaseDomainDto } from "@/lib/types";
import { ActivateBaseDomainButton } from "./ActivateBaseDomainButton";
import { MarkWildcardButton } from "./MarkWildcardButton";
import { DeleteBaseDomainButton } from "./DeleteBaseDomainButton";

export const dynamic = "force-dynamic";

async function fetchDomains(): Promise<
  BaseDomainDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/domains/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as BaseDomainDto[];
}

export default async function BaseDomainsPage() {
  const data = await fetchDomains();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const domains = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Base domains" },
        ]}
        title="Base domains"
        description="FQDN bajo el cual Aethra construye los hostnames. Solo uno puede estar activo a la vez."
        actions={
          <Button asChild>
            <Link href="/settings/domains/new">
              <Plus className="mr-2 h-4 w-4" />
              Nuevo base domain
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado.
          </CardContent>
        </Card>
      ) : domains.length === 0 ? (
        <EmptyState
          icon={<Globe className="h-6 w-6" />}
          title="Aún sin base domains"
          description="Registrá el FQDN bajo el cual Aethra creará hostnames. Después marcá el wildcard como configurado cuando crees el registro DNS."
          action={
            <Button asChild>
              <Link href="/settings/domains/new">
                <Plus className="mr-2 h-4 w-4" />
                Nuevo base domain
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
                <TableHead>Cloudflare zone</TableHead>
                <TableHead>Wildcard DNS</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Creado</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {domains.map((d) => (
                <TableRow key={d.id}>
                  <TableCell className="align-top">
                    <span className="font-mono text-xs">{d.hostname}</span>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {d.id}
                    </div>
                  </TableCell>
                  <TableCell className="align-top text-xs">
                    {d.cloudflareZoneId ? (
                      <Link
                        href={`/cloudflare/${encodeURIComponent(d.cloudflareZoneId)}`}
                        className="font-mono text-foreground hover:text-primary"
                      >
                        {d.cloudflareZoneId}
                      </Link>
                    ) : (
                      <span className="text-muted-foreground">no enlazada</span>
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    {d.wildcardConfigured ? (
                      <Badge variant="success">confirmado</Badge>
                    ) : (
                      <MarkWildcardButton id={d.id} />
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    {d.isActive ? (
                      <Badge variant="success">activo</Badge>
                    ) : (
                      <Badge variant="outline">inactivo</Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(d.createdAt)}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      {!d.isActive ? (
                        <ActivateBaseDomainButton
                          id={d.id}
                          hostname={d.hostname}
                        />
                      ) : null}
                      <DeleteBaseDomainButton
                        id={d.id}
                        hostname={d.hostname}
                        isActive={d.isActive}
                      />
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <Card className="mt-6">
        <CardContent className="p-5 text-sm text-muted-foreground">
          <h3 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Cómo funciona el wildcard
          </h3>
          <p className="mt-2">
            Creá en tu DNS un registro{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              *.tu-base-domain
            </code>{" "}
            apuntando a la IP pública del Edge VM. Cuando lo verifiques, marcá
            <em> Wildcard configurado</em> para que el módulo Proxy use ese
            hostname como wildcard SAN al pedir certificados.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}
