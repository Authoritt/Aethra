import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Pencil, Plug2, Plus } from "lucide-react";
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
import { BackupsTab } from "./BackupsTab";
import { ScheduledJobsTab } from "./ScheduledJobsTab";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";

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
  const t = await getTranslations("pages.services_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { serviceId } = await params;
  const data = await fetchService(serviceId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }

  const service = data;
  const bindings = await fetchBindings(serviceId);
  const activeBindings = bindings.filter((b) => b.revokedAt === null);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("services"), href: "/services" },
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
            <Button asChild variant="outline" size="sm">
              <Link href={`/services/${service.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                Editar
              </Link>
            </Button>
            <DeleteServiceButton
              serviceId={service.id}
              slug={service.slug}
              bindingsCount={activeBindings.length}
            />
          </>
        }
      />

      {service.errorCode ? (
        <Card className="mb-6 border-destructive/30 bg-destructive/5">
          <CardContent className="p-4">
            <div className="text-sm font-medium text-destructive">
              {t("error_prefix")}
              <span className="font-mono">{service.errorCode}</span>
            </div>
            {service.errorMessage ? (
              <p className="mt-1 text-sm text-destructive/90">
                {service.errorMessage}
              </p>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            {t("config_title")}
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <KV label={t("label_image")} mono value={service.image} />
            <KV label={t("label_version")} mono value={service.version} />
            <KV
              label={t("label_internal_port")}
              mono
              value={String(service.internalPort)}
            />
            <KV label={t("label_network")} mono value={service.networkName} />
            <KV label={t("label_container")} mono value={service.containerName} />
            <KV label={t("label_vm_target")} mono value={service.targetVmId} />
            <KV
              label={t("label_exposed")}
              value={service.exposedExternally ? t("exposed_yes") : t("exposed_no")}
            />
            <KV
              label={t("label_provisioned")}
              value={formatDateTime(service.provisionedAt)}
            />
          </div>
        </CardContent>
      </Card>

      <Tabs defaultValue="bindings" className="w-full">
        <TabsList>
          <TabsTrigger value="bindings">{t("tab_bindings")}</TabsTrigger>
          <TabsTrigger value="backups">{t("tab_backups")}</TabsTrigger>
          <TabsTrigger value="scheduled-jobs">Scheduled jobs</TabsTrigger>
        </TabsList>
        <TabsContent value="bindings">
          <section className="space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
                {t("bindings_title", { count: activeBindings.length })}
              </h2>
              <Button asChild size="sm">
                <Link href={`/services/${service.id}/bindings/new`}>
                  <Plus className="mr-2 h-4 w-4" />
                  {t("bind_application")}
                </Link>
              </Button>
            </div>

            {activeBindings.length === 0 ? (
              <EmptyState
                icon={<Plug2 className="h-6 w-6" />}
                title={t("no_bindings_title")}
                description={t("no_bindings_description")}
              />
            ) : (
              <ul className="grid grid-cols-1 gap-3">
                {activeBindings.map((b) => (
                  <BindingCard
                    key={b.id}
                    binding={b}
                    labels={{
                      migrations_hook: t("migrations_hook"),
                      resource: t("label_resource"),
                      env_prefix: t("label_env_prefix"),
                      provisioned: t("label_provisioned"),
                      app_id: t("label_app_id"),
                    }}
                  />
                ))}
              </ul>
            )}
          </section>
        </TabsContent>
        <TabsContent value="backups">
          <BackupsTab serviceId={service.id} />
        </TabsContent>
        <TabsContent value="scheduled-jobs">
          <ScheduledJobsTab serviceId={service.id} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function BindingCard({
  binding,
  labels,
}: {
  binding: ServiceBindingDto;
  labels: {
    migrations_hook: string;
    resource: string;
    env_prefix: string;
    provisioned: string;
    app_id: string;
  };
}) {
  const appLabel =
    binding.instanceSlug ?? binding.instanceId.slice(0, 8);
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
                {binding.hasMigrationsHook ? (
                  <Badge variant="info">{labels.migrations_hook}</Badge>
                ) : null}
              </div>
              <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                <KVInline label={labels.resource} value={binding.resourceName} mono />
                <KVInline
                  label={labels.env_prefix}
                  value={binding.envVarPrefix || "—"}
                  mono
                />
                <KVInline
                  label={labels.provisioned}
                  value={formatDateTime(binding.provisionedAt)}
                />
                <KVInline
                  label={labels.app_id}
                  value={binding.instanceId}
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
