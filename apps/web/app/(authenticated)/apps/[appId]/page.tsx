import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ExternalLink, GitBranch } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { AppEnvironmentOverviewDto, AppOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function AppDetailPage({
  params,
}: {
  params: Promise<{ appId: string }>;
}) {
  const { appId } = await params;
  const [appsResult, envsResult] = await Promise.all([
    serverFetch<AppOverviewDto[]>("/api/ops/apps"),
    serverFetch<AppEnvironmentOverviewDto[]>("/api/ops/app-environments"),
  ]);
  if (appsResult === "unauthorized" || envsResult === "unauthorized") redirect("/login");
  if (!Array.isArray(appsResult)) {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar la app.
          </CardContent>
        </Card>
      </div>
    );
  }

  const app = appsResult.find((x) => x.id === appId);
  if (!app) notFound();
  const envs = Array.isArray(envsResult) ? envsResult.filter((x) => x.appId === appId) : [];
  const tenants = [...new Set(envs.map((x) => x.tenantName))].sort();
  const environments = [...new Set(envs.map((x) => x.environment))].sort();
  const useMatrix = tenants.length > 0 && tenants.length <= 10 && environments.length <= 6;

  return (
    <div className="space-y-8 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Apps", href: "/apps" },
          { label: app.name },
        ]}
        title={app.name}
        description={
          <span className="inline-flex min-w-0 items-center gap-2">
            <GitBranch className="h-4 w-4 shrink-0" />
            <span className="truncate font-mono text-xs">{app.gitRepoUrl}</span>
          </span>
        }
        actions={
          <Button asChild variant="outline">
            <Link href={`/templates/${app.id}`}>Editar definition</Link>
          </Button>
        }
      />

      <div className="grid gap-3 md:grid-cols-4">
        <Metric label="Portfolio" value={app.portfolioName} />
        <Metric label="Tenants" value={String(app.tenantCount)} />
        <Metric label="App environments" value={String(app.appEnvironmentCount)} />
        <Metric label="Issues" value={String(app.issueCount)} />
      </div>

      {useMatrix ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
              Tenant x Environment
            </CardTitle>
          </CardHeader>
          <CardContent className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Tenant</TableHead>
                  {environments.map((env) => (
                    <TableHead key={env} className="font-mono">
                      {env}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {tenants.map((tenant) => (
                  <TableRow key={tenant}>
                    <TableCell className="font-medium">{tenant}</TableCell>
                    {environments.map((env) => {
                      const cell = envs.find((x) => x.tenantName === tenant && x.environment === env);
                      return (
                        <TableCell key={`${tenant}-${env}`}>
                          {cell ? <EnvironmentCell env={cell} /> : <span className="text-muted-foreground">-</span>}
                        </TableCell>
                      );
                    })}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ) : (
        <EnvironmentTable envs={envs} />
      )}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="text-xs uppercase tracking-wider text-muted-foreground">{label}</div>
        <div className="mt-1 truncate text-lg font-semibold">{value}</div>
      </CardContent>
    </Card>
  );
}

function EnvironmentCell({ env }: { env: AppEnvironmentOverviewDto }) {
  return (
    <div className="min-w-44 space-y-2">
      <div className="flex items-center justify-between gap-2">
        <StatusBadge status={env.healthStatus} />
        <Button asChild size="sm" variant="ghost">
          <Link href={`/instances/${env.id}`}>Abrir</Link>
        </Button>
      </div>
      <div className="truncate font-mono text-[11px] text-muted-foreground">
        {env.latestReleaseStatus ?? "no release"}
      </div>
      {env.publicUrl ? (
        <Link
          href={env.publicUrl}
          className="inline-flex max-w-full items-center gap-1 truncate text-xs text-primary"
          target="_blank"
        >
          <ExternalLink className="h-3 w-3 shrink-0" />
          <span className="truncate">{env.publicUrl.replace(/^https?:\/\//, "")}</span>
        </Link>
      ) : null}
    </div>
  );
}

function EnvironmentTable({ envs }: { envs: AppEnvironmentOverviewDto[] }) {
  return (
    <Card>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>App Environment</TableHead>
            <TableHead>Tenant</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Machine</TableHead>
            <TableHead>URL</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {envs.map((env) => (
            <TableRow key={env.id}>
              <TableCell>
                <Link href={`/instances/${env.id}`} className="font-medium hover:text-primary">
                  {env.slug}
                </Link>
              </TableCell>
              <TableCell>{env.tenantName}</TableCell>
              <TableCell className="font-mono text-xs">{env.environment}</TableCell>
              <TableCell><StatusBadge status={env.healthStatus} /></TableCell>
              <TableCell>{env.machineName}</TableCell>
              <TableCell className="max-w-xs truncate font-mono text-xs">{env.publicUrl ?? "-"}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Card>
  );
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const variant = normalized === "healthy" ? "success" : normalized === "failed" ? "destructive" : normalized === "deploying" || normalized === "active" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}
