import { notFound, redirect } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { BuildStatusPill } from "@/components/aethra/build-status-pill";
import { serverFetch } from "@/lib/server-fetch";
import type { BuildDetail } from "@/lib/types";
import { BuildLogsViewer } from "./BuildLogsViewer";

export const dynamic = "force-dynamic";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

export default async function BuildDetailPage({
  params,
}: {
  params: Promise<{ buildId: string }>;
}) {
  const { buildId } = await params;
  const build = await serverFetch<BuildDetail>(`/api/builds/${buildId}`);
  if (build === "unauthorized") redirect("/login");
  if (build === "notfound") notFound();
  if (build === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Error cargando el build.
          </CardContent>
        </Card>
      </div>
    );
  }

  const terminal = TERMINAL_STATUSES.has(build.status);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Builds", href: "/builds" },
          { label: "Template", href: `/templates/${build.templateId}` },
          { label: build.id.slice(0, 8) },
        ]}
        title={build.gitSha.slice(0, 12)}
        description={
          <span className="font-mono text-xs text-muted-foreground">
            {build.id}
          </span>
        }
        actions={<BuildStatusPill status={build.status} />}
      />

      <div className="mb-6 flex flex-wrap gap-2">
        <Badge variant="outline" className="font-mono text-xs">
          ref {build.gitRef}
        </Badge>
        <Badge variant="outline">trigger {build.trigger}</Badge>
        {build.triggeredBy ? (
          <Badge variant="outline">by {build.triggeredBy}</Badge>
        ) : null}
      </div>

      <section className="mb-6 grid grid-cols-1 gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Timing
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3 text-sm">
              <Kv label="Creado" value={formatDate(build.createdAt)} />
              <Kv
                label="Inicio"
                value={build.startedAt ? formatDate(build.startedAt) : "—"}
              />
              <Kv
                label="Fin"
                value={build.finishedAt ? formatDate(build.finishedAt) : "—"}
              />
              <Kv
                label="Duración"
                value={
                  build.buildDurationMs != null
                    ? formatDuration(build.buildDurationMs)
                    : "—"
                }
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
              <Kv label="Image ref" value={build.imageRef ?? "—"} mono />
              <Kv label="Error code" value={build.errorCode ?? "—"} mono />
              <Kv
                label="Error message"
                value={build.errorMessage ?? "—"}
                mono={Boolean(build.errorMessage)}
              />
            </dl>
          </CardContent>
        </Card>
      </section>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Logs
          </h2>
          {terminal ? (
            <span className="text-xs text-muted-foreground">build terminado</span>
          ) : (
            <span className="text-xs text-primary">
              streaming (polling cada 2s)
            </span>
          )}
        </div>
        <BuildLogsViewer buildId={build.id} terminal={terminal} />
      </section>
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

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}
