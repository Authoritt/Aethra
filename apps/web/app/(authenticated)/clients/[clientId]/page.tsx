import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Boxes, Pencil } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { PageHeader } from "@/components/layout/page-header";
import { AutoHostnameInfo } from "@/app/_components/AutoHostnameInfo";
import { ScopedEnvVarsPanel } from "@/components/aethra/ScopedEnvVarsPanel";
import { serverFetch } from "@/lib/server-fetch";
import type { ClientDetail, InstanceSummary } from "@/lib/types";
import { DeleteClientButton } from "./DeleteClientButton";

export const dynamic = "force-dynamic";

export default async function ClientDetailPage({
  params,
}: {
  params: Promise<{ clientId: string }>;
}) {
  const t = await getTranslations("pages.clients_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const tCommon = await getTranslations("common");
  const { clientId } = await params;

  const [clientResult, instancesResult] = await Promise.all([
    serverFetch<ClientDetail>(`/api/clients/${clientId}`),
    serverFetch<InstanceSummary[]>(`/api/clients/${clientId}/instances`),
  ]);

  if (clientResult === "unauthorized") redirect("/login");
  if (clientResult === "notfound") notFound();
  if (clientResult === "error") {
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

  const client = clientResult;
  const instances = Array.isArray(instancesResult) ? instancesResult : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("projects"), href: "/projects" },
          { label: tCommon("go_to_project"), href: `/projects/${client.projectId}` },
          { label: client.displayName },
        ]}
        title={client.displayName}
        description={
          <>
            <span className="font-mono text-xs">{client.slug}</span>
            {client.description ? (
              <>
                <span className="mx-2 text-muted-foreground/50">·</span>
                {client.description}
              </>
            ) : null}
          </>
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="outline" size="sm">
              <Link href={`/clients/${client.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                Editar
              </Link>
            </Button>
            <DeleteClientButton clientId={client.id} displayName={client.displayName} />
          </div>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        {client.contactEmail ? (
          <Badge variant="outline">{client.contactEmail}</Badge>
        ) : null}
        {client.billingTag ? (
          <Badge variant="outline" className="font-mono">
            {client.billingTag}
          </Badge>
        ) : null}
        <Badge variant="outline">{t("instances_count", { count: instances.length })}</Badge>
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">{t("tab_overview")}</TabsTrigger>
          <TabsTrigger value="instances">
            {t("tab_instances", { count: instances.length })}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <Card>
            <CardContent className="grid grid-cols-1 gap-4 p-6 md:grid-cols-3">
              <Kv label={t("contact_email")} value={client.contactEmail ?? "—"} />
              <Kv
                label={t("billing_tag")}
                value={client.billingTag ?? "—"}
                mono={Boolean(client.billingTag)}
              />
              <Kv label={t("instances")} value={String(instances.length)} />
            </CardContent>
          </Card>
          <div className="mt-4">
            <ScopedEnvVarsPanel scopeType="client" scopeId={client.id} />
          </div>
        </TabsContent>

        <TabsContent value="instances" className="mt-6">
          {instances.length === 0 ? (
            <EmptyState
              icon={<Boxes className="h-6 w-6" />}
              title={t("empty_title")}
              description={t("empty_description")}
            />
          ) : (
            <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
              {instances.map((inst) => (
                <li key={inst.id}>
                  <Link
                    href={`/instances/${inst.id}`}
                    className="group block rounded-md border border-border bg-card p-4 transition-colors hover:border-primary/40"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <h3 className="truncate font-mono text-xs text-foreground">
                        {inst.slug}
                      </h3>
                      <Badge variant="outline">{inst.environment}</Badge>
                    </div>
                    <p className="mt-2 font-mono text-[11px] text-muted-foreground">
                      {t("template_label", { id: inst.templateId.slice(0, 8) })}
                    </p>
                    <div className="mt-2">
                      <AutoHostnameInfo
                        autoHostname={inst.autoHostname}
                        customDomain={inst.customDomain}
                      />
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Kv({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd
        className={`mt-0.5 break-all text-foreground ${mono ? "font-mono text-xs" : "text-sm"}`}
      >
        {value}
      </dd>
    </div>
  );
}
