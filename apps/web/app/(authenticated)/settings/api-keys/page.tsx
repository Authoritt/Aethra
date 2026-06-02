import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Key, Plus } from "lucide-react";
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
import type { ApiKeySummary } from "@/lib/types";
import { ApiKeyStatusPill, deriveStatus } from "./ApiKeyStatusPill";
import { RevokeKeyButton } from "./RevokeKeyButton";

export const dynamic = "force-dynamic";

async function fetchKeys(): Promise<
  ApiKeySummary[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/api-keys`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ApiKeySummary[];
}

export default async function ApiKeysPage() {
  const data = await fetchKeys();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const keys = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "API keys" },
        ]}
        title="API keys"
        description="Tokens portadores para integrar herramientas externas y agentes con la API de Aethra. El secret se muestra una única vez al crearlas."
        actions={
          <Button asChild>
            <Link href="/settings/api-keys/new">
              <Plus className="mr-2 h-4 w-4" />
              Crear API key
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
      ) : keys.length === 0 ? (
        <EmptyState
          icon={Key}
          title="Aún sin API keys"
          description="Creá tu primera API key para que tus integraciones, scripts y agentes IA puedan llamar a la API de Aethra."
          action={
            <Button asChild>
              <Link href="/settings/api-keys/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear API key
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Prefix</TableHead>
                <TableHead>Scopes</TableHead>
                <TableHead>Creada</TableHead>
                <TableHead>Último uso</TableHead>
                <TableHead>Expira</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((key) => {
                const status = deriveStatus(key);
                return (
                  <TableRow key={key.id}>
                    <TableCell className="align-top">
                      <div className="font-medium">{key.name}</div>
                      <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                        {key.id}
                      </div>
                    </TableCell>
                    <TableCell className="align-top">
                      <Badge
                        variant="outline"
                        className="font-mono text-[11px]"
                      >
                        {key.key_prefix}…
                      </Badge>
                    </TableCell>
                    <TableCell className="align-top">
                      <ScopesPills scopes={key.scopes} />
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatDate(key.created_at)}
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatRelative(key.last_used_at)}
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatExpires(key.expires_at)}
                    </TableCell>
                    <TableCell className="align-top">
                      <ApiKeyStatusPill status={status} />
                    </TableCell>
                    <TableCell className="align-top text-right">
                      <RevokeKeyButton
                        id={key.id}
                        name={key.name}
                        alreadyRevoked={status === "revoked"}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      <Card className="mt-6">
        <CardContent className="p-5 text-sm text-muted-foreground">
          <h3 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Cómo usar una API key
          </h3>
          <p className="mt-2">
            Enviá el secret en el header{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              Authorization: Bearer aethra_…
            </code>{" "}
            o como{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              X-Api-Key
            </code>{" "}
            según la integración.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

function ScopesPills({ scopes }: { scopes: string[] }) {
  if (scopes.length === 0) {
    return <span className="text-xs text-muted-foreground">(ninguno)</span>;
  }
  if (scopes.includes("*")) {
    return <Badge variant="warning">admin (*)</Badge>;
  }
  const max = 4;
  const visible = scopes.slice(0, max);
  const overflow = scopes.length - max;
  return (
    <div className="flex flex-wrap gap-1">
      {visible.map((s) => (
        <Badge key={s} variant="outline" className="font-mono text-[10px]">
          {s}
        </Badge>
      ))}
      {overflow > 0 ? (
        <Badge variant="outline" className="font-mono text-[10px]">
          +{overflow}
        </Badge>
      ) : null}
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

function formatExpires(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  return formatDate(iso);
}
