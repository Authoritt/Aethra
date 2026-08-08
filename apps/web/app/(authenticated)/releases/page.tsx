import Link from "next/link";
import { redirect } from "next/navigation";
import { X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { MultiSelectFilter } from "@/components/aethra/MultiSelectFilter";
import { SavedViewsMenu } from "@/components/aethra/SavedViewsMenu";
import { getTranslations } from "next-intl/server";
import { serverFetch } from "@/lib/server-fetch";
import type { AppOverviewDto, ReleaseOverviewDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface ReleaseFilters {
  q?: string;
  status?: string;
  appId?: string;
  gitRef?: string;
}

export default async function ReleasesPage({
  searchParams,
}: {
  searchParams: Promise<ReleaseFilters>;
}) {
  const t = await getTranslations("pages.releases");
  const c = await getTranslations("common");
  const filters = await searchParams;
  const query = buildQuery(filters);
  const [data, appsData] = await Promise.all([
    serverFetch<ReleaseOverviewDto[]>(`/api/ops/releases${query}`),
    serverFetch<AppOverviewDto[]>("/api/ops/apps"),
  ]);
  if (data === "unauthorized" || appsData === "unauthorized") {
    redirect("/login");
  }
  const releases = Array.isArray(data) ? data : [];
  const apps = Array.isArray(appsData) ? appsData : [];
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={<SavedViewsMenu storageKey="aethra.savedViews.releases" />}
      />

      <Card>
        <CardContent className="p-4">
          <form method="get" className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="q">{c("search")}</Label>
              <Input
                id="q"
                name="q"
                defaultValue={filters.q ?? ""}
                placeholder="SHA, app, tenant, environment"
                className="w-64"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="status">{t("filter_status")}</Label>
              <MultiSelectFilter
                id="status"
                name="status"
                value={filters.status}
                allLabel={c("all")}
                options={[
                  { value: "healthy", label: "Healthy" },
                  { value: "active", label: "Active" },
                  { value: "failed", label: "Failed" },
                  { value: "unknown", label: "Unknown" },
                ]}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="appId">{t("filter_app")}</Label>
              <MultiSelectFilter
                id="appId"
                name="appId"
                value={filters.appId}
                allLabel={c("all_f")}
                className="max-w-64"
                options={apps.map((app) => ({ value: app.id, label: app.name }))}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="gitRef">{t("filter_ref")}</Label>
              <Input
                id="gitRef"
                name="gitRef"
                defaultValue={filters.gitRef ?? ""}
                placeholder="main, refs/heads/main"
                className="w-56 font-mono text-xs"
              />
            </div>
            <Button type="submit">{c("filter")}</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/releases">
                  <X className="mr-2 h-4 w-4" />
                  {c("clear")}
                </Link>
              </Button>
            ) : null}
          </form>
        </CardContent>
      </Card>

      {data === "error" || data === "notfound" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Status</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Ref</TableHead>
                <TableHead>SHA</TableHead>
                <TableHead>Fan-out</TableHead>
                <TableHead>Trigger</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {releases.map((release) => (
                <TableRow key={release.id}>
                  <TableCell><StatusBadge status={release.status} /></TableCell>
                  <TableCell>
                    {release.appId ? (
                      <Link href={`/apps/${release.appId}`} className="font-medium hover:text-primary">
                        {release.appName}
                      </Link>
                    ) : (
                      <span>{release.appName}</span>
                    )}
                  </TableCell>
                  <TableCell className="font-mono text-xs">{release.gitRef}</TableCell>
                  <TableCell>
                    <Link href={`/releases/${release.id}`} className="font-mono text-xs text-primary">
                      {release.shortSha}
                    </Link>
                  </TableCell>
                  <TableCell className="text-xs">
                    {release.completedCount} ok / {release.failedCount} failed / {release.activeCount} active
                  </TableCell>
                  <TableCell className="text-xs">{release.trigger}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">{formatDate(release.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === "healthy"
      ? "success"
      : status === "failed"
        ? "destructive"
        : status === "active"
          ? "warning"
          : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{status}</Badge>;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function buildQuery(filters: ReleaseFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
