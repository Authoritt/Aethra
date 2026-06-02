import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { Boxes } from "lucide-react";
import { Badge } from "@/components/ui/badge";
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
import { serverFetch } from "@/lib/server-fetch";
import type { ClientDetail, InstanceSummary } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ClientDetailPage({
  params,
}: {
  params: Promise<{ clientId: string }>;
}) {
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
            Error cargando el client.
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
          { label: "Proyectos", href: "/projects" },
          { label: "Proyecto", href: `/projects/${client.projectId}` },
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
        <Badge variant="outline">{client.instanceCount} instances</Badge>
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="instances">
            Instances ({instances.length})
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-6">
          <Card>
            <CardContent className="grid grid-cols-1 gap-4 p-6 md:grid-cols-3">
              <Kv label="Contact email" value={client.contactEmail ?? "—"} />
              <Kv
                label="Billing tag"
                value={client.billingTag ?? "—"}
                mono={Boolean(client.billingTag)}
              />
              <Kv label="Instances" value={String(client.instanceCount)} />
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="instances" className="mt-6">
          {instances.length === 0 ? (
            <EmptyState
              icon={Boxes}
              title="Sin instances"
              description="Este client aún no tiene instancias. Creales una desde el detalle de un template."
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
                      template {inst.templateId.slice(0, 8)}
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
