import Link from "next/link";
import { redirect } from "next/navigation";
import { AppWindow, ChevronRight, GitBranch } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { getTranslations } from "next-intl/server";
import { serverFetch } from "@/lib/server-fetch";
import type { AppOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function AppsPage() {
  const t = await getTranslations("pages.apps");
  const data = await serverFetch<AppOverviewDto[]>("/api/ops/apps");
  if (data === "unauthorized") redirect("/login");
  const apps = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-8 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Button asChild variant="outline">
            <Link href="/templates">{t("action_definitions")}</Link>
          </Button>
        }
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      ) : apps.length === 0 ? (
        <EmptyState
          icon={<AppWindow className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/templates">{t("empty_action")}</Link>
            </Button>
          }
        />
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {apps.map((app) => (
            <Card key={app.id} className="transition-colors hover:border-primary/40">
              <CardHeader className="space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <CardTitle className="truncate text-base">{app.name}</CardTitle>
                    <p className="mt-1 truncate font-mono text-xs text-muted-foreground">
                      {app.slug}
                    </p>
                  </div>
                  <StatusBadge status={app.status} />
                </div>
                <div className="flex flex-wrap gap-2">
                  <Badge variant="outline">{app.portfolioName}</Badge>
                  <Badge variant="outline">{t("badge_tenants", { count: app.tenantCount })}</Badge>
                  <Badge variant="outline">{t("badge_envs", { count: app.appEnvironmentCount })}</Badge>
                </div>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="flex items-center gap-2 truncate text-xs text-muted-foreground">
                  <GitBranch className="h-3.5 w-3.5 shrink-0" />
                  <span className="truncate font-mono">{app.defaultBranch}</span>
                </div>
                <div className="flex flex-wrap gap-1">
                  {app.environments.map((env) => (
                    <Badge key={env} variant="secondary" className="font-mono text-[10px]">
                      {env}
                    </Badge>
                  ))}
                </div>
                <div className="flex items-center justify-between gap-2 pt-1">
                  <span className="text-xs text-muted-foreground">
                    {t("issues", { count: app.issueCount })}
                  </span>
                  <Button asChild size="sm" variant="ghost">
                    <Link href={`/apps/${app.id}`}>
                      {t("open")}
                      <ChevronRight className="ml-1 h-4 w-4" />
                    </Link>
                  </Button>
                </div>
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
    normalized === "healthy"
      ? "success"
      : normalized === "failed"
        ? "destructive"
        : normalized === "active"
          ? "warning"
          : "outline";

  return (
    <Badge variant={variant} className="shrink-0 font-mono text-[10px] uppercase">
      {status}
    </Badge>
  );
}
