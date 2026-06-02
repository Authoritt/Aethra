import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { Plus } from "lucide-react";
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
import type { CloudflareZoneDetailDto, DnsRecordDto } from "@/lib/types";
import { ZoneStatusPill } from "../ZoneStatusPill";
import { SyncZoneButton } from "../SyncZoneButton";
import { DeleteRecordButton } from "../DeleteRecordButton";
import { RotateTokenButton } from "./RotateTokenButton";
import { DeleteZoneButton } from "./DeleteZoneButton";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchZone(
  zoneId: string,
): Promise<CloudflareZoneDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/cloudflare/zones/${zoneId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as CloudflareZoneDetailDto;
}

export default async function ZoneDetailPage({
  params,
}: {
  params: Promise<{ zoneId: string }>;
}) {
  const { zoneId } = await params;
  const data = await fetchZone(zoneId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();
  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando la zona.
          </CardContent>
        </Card>
      </div>
    );
  }

  const zone = data;
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Cloudflare", href: "/cloudflare" },
          { label: zone.name },
        ]}
        title={zone.name}
        description={
          <span className="font-mono text-xs">
            zone_id: {zone.external_zone_id}
            <span className="mx-2 text-muted-foreground/50">·</span>
            account: {zone.account_id}
          </span>
        }
        actions={
          <>
            <ZoneStatusPill status={zone.status} />
            <SyncZoneButton zoneId={zone.id} />
            <RotateTokenButton zoneId={zone.id} />
            <DeleteZoneButton
              zoneId={zone.id}
              name={zone.name}
              recordsCount={zone.records.length}
            />
          </>
        }
      />

      <section className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            DNS Records ({zone.records.length})
          </h2>
          <Button asChild size="sm">
            <Link href={`/cloudflare/${zone.id}/records/new`}>
              <Plus className="mr-2 h-4 w-4" />
              Crear record
            </Link>
          </Button>
        </div>

        {zone.records.length === 0 ? (
          <EmptyState
            title="Aún sin records gestionados"
            description="Creá uno o sincronizá desde Cloudflare para importar los existentes."
          />
        ) : (
          <Card>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Nombre</TableHead>
                  <TableHead>Contenido</TableHead>
                  <TableHead>TTL</TableHead>
                  <TableHead>Proxied</TableHead>
                  <TableHead className="text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {zone.records.map((r) => (
                  <RecordRow key={r.id} record={r} />
                ))}
              </TableBody>
            </Table>
          </Card>
        )}
      </section>
    </div>
  );
}

function RecordRow({ record }: { record: DnsRecordDto }) {
  return (
    <TableRow>
      <TableCell>
        <Badge variant="outline" className="font-mono text-[10px]">
          {record.type}
        </Badge>
      </TableCell>
      <TableCell className="font-mono text-xs">{record.name}</TableCell>
      <TableCell
        className="max-w-[24rem] truncate font-mono text-xs text-muted-foreground"
        title={record.content}
      >
        {record.content}
      </TableCell>
      <TableCell className="font-mono text-xs text-muted-foreground">
        {record.ttl === 1 ? "auto" : record.ttl}
      </TableCell>
      <TableCell>
        {record.proxied ? (
          <Badge variant="warning">proxied</Badge>
        ) : (
          <Badge variant="outline">dns only</Badge>
        )}
      </TableCell>
      <TableCell className="text-right">
        <DeleteRecordButton recordId={record.id} name={record.name} />
      </TableCell>
    </TableRow>
  );
}
