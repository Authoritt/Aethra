import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { Plug2, Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type {
  ManagedServiceDetailDto,
  ServiceBindingDto,
} from "@/lib/types";
import { ServiceStatusPill } from "../ServiceStatusPill";
import { BindingActions } from "../BindingActions";
import { DeleteServiceButton } from "./DeleteServiceButton";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchService(
  serviceId: string,
): Promise<ManagedServiceDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/${serviceId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as ManagedServiceDetailDto;
}

async function fetchBindings(serviceId: string): Promise<ServiceBindingDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/${serviceId}/bindings`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return [];
  return (await res.json()) as ServiceBindingDto[];
}

export default async function ServiceDetailPage({
  params,
}: {
  params: Promise<{ serviceId: string }>;
}) {
  const { serviceId } = await params;
  const data = await fetchService(serviceId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el servicio.
          </CardContent>
        </Card>
      </div>
    );
  }

  const service = data;
  const bindings = await fetchBindings(serviceId);
  const activeBindings = bindings.filter((b) => b.revoked_at === null);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Servicios", href: "/services" },
          { label: service.slug },
        ]}
        title={service.name}
        description={
          <>
            <span className="font-mono text-xs">{service.slug}</span>
            <span className="mx-2 text-muted-foreground/50">·</span>
            <span className="font-mono text-[10px] uppercase">
              {service.type}
            </span>
          </>
        }
        actions={
          <>
            <ServiceStatusPill status={service.status} />
            <DeleteServiceButton
              serviceId={service.id}
              slug={service.slug}
              bindingsCount={activeBindings.length}
            />
          </>
        }
      />

      {service.error_code ? (
        <Card className="mb-6 border-destructive/30 bg-destructive/5">
          <CardContent className="p-4">
            <div className="text-sm font-medium text-destructive">
              Error: <span className="font-mono">{service.error_code}</span>
            </div>
            {service.error_message ? (
              <p className="mt-1 text-sm text-destructive/90">
                {service.error_message}
              </p>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Configuración
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <KV label="Imagen" mono value={service.image} />
            <KV label="Versión" mono value={service.version} />
            <KV
              label="Puerto interno"
              mono
              value={String(service.internal_port)}
            />
            <KV label="Network" mono value={service.network_name} />
            <KV label="Container" mono value={service.container_name} />
            <KV label="VM target" mono value={service.target_vm_id} />
            <KV
              label="Expuesto externamente"
              value={service.exposed_externally ? "Sí" : "No (interno)"}
            />
            <KV
              label="Provisionado"
              value={formatDateTime(service.provisioned_at)}
            />
          </div>
        </CardContent>
      </Card>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Bindings ({activeBindings.length})
          </h2>
          <Button asChild size="sm">
            <Link href={`/services/${service.id}/bindings/new`}>
              <Plus className="mr-2 h-4 w-4" />
              Bindear aplicación
            </Link>
          </Button>
        </div>

        {activeBindings.length === 0 ? (
          <EmptyState
            icon={Plug2}
            title="Sin bindings activos"
            description="Bindea una application para que pueda consumir este servicio."
          />
        ) : (
          <ul className="grid grid-cols-1 gap-3">
            {activeBindings.map((b) => (
              <BindingCard key={b.id} binding={b} />
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function BindingCard({ binding }: { binding: ServiceBindingDto }) {
  const appLabel =
    binding.application_slug ?? binding.application_id.slice(0, 8);
  return (
    <li>
      <Card>
        <CardContent className="p-5">
          <div className="flex items-start justify-between gap-4">
            <div className="flex min-w-0 flex-1 flex-col gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="truncate text-sm font-semibold text-foreground">
                  {appLabel}
                </h3>
                <PermissionsChip permissions={binding.permissions} />
                {binding.has_migrations_hook ? (
                  <Badge variant="info">migrations hook</Badge>
                ) : null}
              </div>
              <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                <KVInline label="Resource" value={binding.resource_name} mono />
                <KVInline
                  label="Env prefix"
                  value={binding.env_var_prefix || "—"}
                  mono
                />
                <KVInline
                  label="Provisionado"
                  value={formatDateTime(binding.provisioned_at)}
                />
                <KVInline
                  label="Application ID"
                  value={binding.application_id}
                  mono
                />
              </dl>
            </div>
            <BindingActions bindingId={binding.id} appLabel={appLabel} />
          </div>
        </CardContent>
      </Card>
    </li>
  );
}

function PermissionsChip({
  permissions,
}: {
  permissions: ServiceBindingDto["permissions"];
}) {
  const variant: Record<
    ServiceBindingDto["permissions"],
    "warning" | "success" | "outline"
  > = {
    Owner: "warning",
    ReadWrite: "success",
    ReadOnly: "outline",
  };
  return <Badge variant={variant[permissions]}>{permissions}</Badge>;
}

function KV({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-md border border-border bg-muted/30 p-3">
      <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </div>
      <div
        className={`mt-1 truncate text-sm text-foreground ${mono ? "font-mono" : ""}`}
        title={value}
      >
        {value}
      </div>
    </div>
  );
}

function KVInline({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </span>
      <span
        className={`truncate text-foreground ${mono ? "font-mono" : ""}`}
        title={value}
      >
        {value}
      </span>
    </div>
  );
}

function formatDateTime(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
