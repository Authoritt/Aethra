import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { DeploymentStatusPill } from "@/components/aethra/deployment-status-pill";
import { serverFetch } from "@/lib/server-fetch";
import type { DeploymentDetail } from "@/lib/types";
import { DeploymentLivePoll } from "./DeploymentLivePoll";

export const dynamic = "force-dynamic";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

export default async function DeploymentDetailPage({
  params,
}: {
  params: Promise<{ deploymentId: string }>;
}) {
  const { deploymentId } = await params;
  const deployment = await serverFetch<DeploymentDetail>(
    `/api/deployments/${deploymentId}`,
  );
  if (deployment === "unauthorized") redirect("/login");
  if (deployment === "notfound") notFound();
  if (deployment === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el deployment.
          </CardContent>
        </Card>
      </div>
    );
  }

  const terminal = TERMINAL_STATUSES.has(deployment.status);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Deployments", href: "/deployments" },
          {
            label: "Instance",
            href: `/instances/${deployment.instanceId}`,
          },
          { label: deployment.id.slice(0, 8) },
        ]}
        title={deployment.id.slice(0, 12)}
        description={
          <span className="font-mono text-xs text-muted-foreground">
            {deployment.id}
          </span>
        }
        actions={
          <>
            <DeploymentStatusPill status={deployment.status} />
            {!terminal ? (
              <DeploymentLivePoll deploymentId={deployment.id} />
            ) : null}
          </>
        }
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline">trigger {deployment.trigger}</Badge>
      </div>

      <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Timing
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv label="Creado" value={formatDate(deployment.createdAt)} />
              <Kv
                label="Fin"
                value={
                  deployment.finishedAt
                    ? formatDate(deployment.finishedAt)
                    : "—"
                }
              />
            </dl>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Imagen
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv label="Nueva" value={deployment.newImageRef} mono />
              <Kv
                label="Anterior"
                value={deployment.oldImageRef ?? "—"}
                mono={Boolean(deployment.oldImageRef)}
              />
            </dl>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Container
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv
                label="Nuevo"
                value={deployment.newContainerId ?? "—"}
                mono={Boolean(deployment.newContainerId)}
              />
              <Kv
                label="Anterior"
                value={deployment.oldContainerId ?? "—"}
                mono={Boolean(deployment.oldContainerId)}
              />
            </dl>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Resultado
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv label="Error code" value={deployment.errorCode ?? "—"} mono />
              <Kv
                label="Error message"
                value={deployment.errorMessage ?? "—"}
                mono={Boolean(deployment.errorMessage)}
              />
            </dl>
          </CardContent>
        </Card>
      </section>

      <Card className="mt-6">
        <CardHeader>
          <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Logs del deploy
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            El detalle del deploy no expone logs propios en el contrato actual.
            Para ver los logs del build asociado y entender el origen de la
            imagen,{" "}
            <Link
              href={`/builds/${deployment.buildId}`}
              className="text-primary underline-offset-4 hover:underline"
            >
              abrí el build {deployment.buildId.slice(0, 8)}
            </Link>
            .
          </p>
        </CardContent>
      </Card>
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

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
