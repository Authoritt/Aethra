import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Plug2, Plus, RefreshCw } from "lucide-react";
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
import type { IntegrationCredentialDto } from "@/lib/types";
import { DeleteIntegrationButton } from "./DeleteIntegrationButton";

export const dynamic = "force-dynamic";

async function fetchCredentials(): Promise<
  IntegrationCredentialDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/integrations/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as IntegrationCredentialDto[];
}

export default async function IntegrationsPage() {
  const data = await fetchCredentials();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const creds = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Integraciones" },
        ]}
        title="Integraciones"
        description={
          <>
            Credenciales externas cifradas con DataProtection. Otros módulos las
            resuelven por <span className="font-mono">name</span> (formato{" "}
            <span className="font-mono">namespace:slug</span>).
          </>
        }
        actions={
          <Button asChild>
            <Link href="/settings/integrations/new">
              <Plus className="mr-2 h-4 w-4" />
              Nueva credencial
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
      ) : creds.length === 0 ? (
        <EmptyState
          icon={Plug2}
          title="Aún sin credenciales"
          description="Centralizá acá las credenciales que comparten varios módulos (Cloudflare, GitHub, registries...). El valor en claro solo se ve una vez."
          action={
            <Button asChild>
              <Link href="/settings/integrations/new">
                <Plus className="mr-2 h-4 w-4" />
                Nueva credencial
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Tipo</TableHead>
                <TableHead>Display</TableHead>
                <TableHead>Creada</TableHead>
                <TableHead>Última rotación</TableHead>
                <TableHead>Último uso</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {creds.map((c) => (
                <TableRow key={c.id}>
                  <TableCell className="align-top">
                    <Badge
                      variant="outline"
                      className="font-mono text-[11px]"
                    >
                      {c.name}
                    </Badge>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {c.id}
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className="font-mono text-[10px] uppercase">
                      {c.type}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="text-foreground">{c.displayName}</div>
                    {c.description ? (
                      <div className="mt-0.5 text-xs text-muted-foreground">
                        {c.description}
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(c.createdAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.rotatedAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.lastUsedAt)}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      <Button asChild variant="outline" size="sm">
                        <Link
                          href={`/settings/integrations/${encodeURIComponent(c.id)}/rotate`}
                        >
                          <RefreshCw className="mr-2 h-4 w-4" />
                          Rotar
                        </Link>
                      </Button>
                      <DeleteIntegrationButton id={c.id} name={c.name} />
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
            Cómo resolver una credencial
          </h3>
          <p className="mt-2">
            Desde C# inyectá{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              IIntegrationCredentialResolver
            </code>{" "}
            y llamá{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              GetSecretAsync(&quot;cloudflare:default&quot;, ct)
            </code>
            . El valor en claro nunca se persiste; solo se descifra en memoria
            al consumirlo.
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

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "hace unos seg.";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  return `hace ${days} d`;
}
