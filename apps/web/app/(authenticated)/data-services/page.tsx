import Link from "next/link";
import { redirect } from "next/navigation";
import { Database, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageHeader } from "@/components/layout/page-header";
import { MultiSelectFilter } from "@/components/aethra/MultiSelectFilter";
import { SavedViewsMenu } from "@/components/aethra/SavedViewsMenu";
import { serverFetch } from "@/lib/server-fetch";
import type { DataServiceOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface DataServiceFilters {
  q?: string;
  status?: string;
  type?: string;
}

export default async function DataServicesPage({
  searchParams,
}: {
  searchParams: Promise<DataServiceFilters>;
}) {
  const filters = await searchParams;
  const query = buildQuery(filters);
  const data = await serverFetch<DataServiceOverviewDto[]>(`/api/ops/data-services${query}`);
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error" || data === "notfound";
  const services = Array.isArray(data) ? data : [];
  const typeOptions = Array.from(new Set(["Postgres", "Redis", "RabbitMQ", ...services.map((service) => service.type)]));
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Data Services"
        description="Servicios de datos y bindings vistos desde las apps y ambientes que los consumen."
        actions={
          <div className="flex flex-wrap gap-2">
            <SavedViewsMenu storageKey="aethra.savedViews.dataServices" />
            <Button asChild variant="outline">
              <Link href="/services">Servicios tecnicos</Link>
            </Button>
          </div>
        }
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
                placeholder="servicio, app, tenant, recurso"
                className="w-72"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="status">Status</Label>
              <MultiSelectFilter
                id="status"
                name="status"
                value={filters.status}
                allLabel="Todos"
                options={[
                  { value: "Ready", label: "Ready" },
                  { value: "Provisioning", label: "Provisioning" },
                  { value: "Failed", label: "Failed" },
                  { value: "Stopped", label: "Stopped" },
                ]}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="type">Type</Label>
              <MultiSelectFilter
                id="type"
                name="type"
                value={filters.type}
                allLabel="Todos"
                options={typeOptions.map((type) => ({ value: type, label: type }))}
              />
            </div>
            <Button type="submit">Filtrar</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/data-services">
                  <X className="mr-2 h-4 w-4" />
                  Limpiar
                </Link>
              </Button>
            ) : null}
          </form>
        </CardContent>
      </Card>

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar Data Services.
          </CardContent>
        </Card>
      ) : services.length === 0 ? (
        <EmptyState
          icon={<Database className="h-6 w-6" />}
          title="Sin Data Services"
          description={
            hasFilters
              ? "No hay servicios que coincidan con los filtros actuales."
              : "Cuando existan servicios gestionados apareceran con sus consumidores."
          }
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          {services.map((service) => (
            <Card key={service.id}>
              <CardContent className="space-y-4 p-5">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Link href={`/services/${service.id}`} className="truncate text-base font-semibold hover:text-primary">
                      {service.name}
                    </Link>
                    <p className="mt-1 font-mono text-xs text-muted-foreground">
                      {service.type} / {service.version}
                    </p>
                  </div>
                  <StatusBadge status={service.status} />
                </div>

                <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
                  <SmallStat label="Bindings" value={service.activeBindingCount.toString()} />
                  <SmallStat label="Machine" value={service.machineId.slice(0, 8)} mono />
                  <SmallStat label="External" value={service.exposedExternally ? "yes" : "no"} />
                  <SmallStat label="Backup" value={service.lastBackupAt ? formatDate(service.lastBackupAt) : "-"} />
                </div>

                {service.errorMessage ? (
                  <p className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
                    {service.errorMessage}
                  </p>
                ) : null}

                {service.bindings.length > 0 ? (
                  <div className="space-y-2">
                    <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      Consumers
                    </p>
                    {service.bindings.slice(0, 4).map((binding) => (
                      <Link
                        key={binding.id}
                        href={`/instances/${binding.appEnvironmentId}`}
                        className="flex items-center justify-between gap-3 rounded-md border p-3 transition-colors hover:border-primary/40"
                      >
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium">
                            {binding.appName ?? binding.appEnvironmentSlug ?? binding.appEnvironmentId}
                          </p>
                          <p className="truncate text-xs text-muted-foreground">
                            {[binding.tenantName, binding.environment, binding.resourceName].filter(Boolean).join(" / ")}
                          </p>
                        </div>
                        <StatusBadge status={binding.status} />
                      </Link>
                    ))}
                    {service.bindings.length > 4 ? (
                      <p className="text-xs text-muted-foreground">
                        +{service.bindings.length - 4} consumers mas
                      </p>
                    ) : null}
                  </div>
                ) : (
                  <p className="rounded-md border border-dashed p-3 text-sm text-muted-foreground">
                    Sin consumers activos.
                  </p>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant =
    normalized === "ready"
      ? "success"
      : normalized === "failed" || normalized === "revoked"
        ? "destructive"
        : normalized === "provisioning"
          ? "warning"
          : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function SmallStat({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-md border bg-muted/20 p-3">
      <p className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <p className={`mt-1 truncate text-foreground ${mono ? "font-mono" : ""}`} title={value}>
        {value}
      </p>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("es-CO", {
    month: "short",
    day: "2-digit",
  }).format(new Date(value));
}

function buildQuery(filters: DataServiceFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
