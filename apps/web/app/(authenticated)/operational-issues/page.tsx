import Link from "next/link";
import { redirect } from "next/navigation";
import type { ReactNode } from "react";
import { AlertTriangle, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
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
import type { AppOverviewDto, OperationalIssueDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface IssueFilters {
  q?: string;
  severity?: string;
  resourceType?: string;
  appId?: string;
}

export default async function OperationalIssuesPage({
  searchParams,
}: {
  searchParams: Promise<IssueFilters>;
}) {
  const t = await getTranslations("pages.operational_issues");
  const c = await getTranslations("common");
  const filters = await searchParams;
  const query = buildQuery(filters);
  const [data, appsData] = await Promise.all([
    serverFetch<OperationalIssueDto[]>(`/api/ops/operational-issues${query}`),
    serverFetch<AppOverviewDto[]>("/api/ops/apps"),
  ]);
  if (data === "unauthorized" || appsData === "unauthorized") {
    redirect("/login");
  }
  const issues = Array.isArray(data) ? data : [];
  const apps = Array.isArray(appsData) ? appsData : [];
  const resourceTypes = Array.from(new Set(["AppEnvironment", "Release", "PublicEndpoint", "Machine", ...issues.map((i) => i.resourceType)]));
  const hasFilters = Object.values(filters).some((value) => Boolean(value));

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={<SavedViewsMenu storageKey="aethra.savedViews.operationalIssues" />}
      />

      <Card>
        <CardContent className="space-y-4 p-4">
          <div className="flex flex-wrap gap-2">
            <QuickFilter href="/operational-issues?severity=critical" active={filters.severity === "critical"}>
              Critical
            </QuickFilter>
            <QuickFilter href="/operational-issues?resourceType=PublicEndpoint" active={filters.resourceType === "PublicEndpoint"}>
              Public Access
            </QuickFilter>
            <QuickFilter href="/operational-issues?resourceType=Machine" active={filters.resourceType === "Machine"}>
              Machines
            </QuickFilter>
            <QuickFilter href="/operational-issues?q=config." active={filters.q === "config."}>
              Config drift
            </QuickFilter>
          </div>
          <form method="get" className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="q">{c("search")}</Label>
              <Input
                id="q"
                name="q"
                defaultValue={filters.q ?? ""}
                placeholder={t("filter_search_placeholder")}
                className="w-64"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="severity">{t("filter_severity")}</Label>
              <MultiSelectFilter
                id="severity"
                name="severity"
                value={filters.severity}
                allLabel={c("all_f")}
                options={[
                  { value: "critical", label: "Critical" },
                  { value: "warning", label: "Warning" },
                ]}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="resourceType">{t("filter_resource")}</Label>
              <MultiSelectFilter
                id="resourceType"
                name="resourceType"
                value={filters.resourceType}
                allLabel={c("all")}
                options={resourceTypes.map((type) => ({ value: type, label: type }))}
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
            <Button type="submit">{c("filter")}</Button>
            {hasFilters ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/operational-issues">
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
            No se pudo cargar issues operativos.
          </CardContent>
        </Card>
      ) : issues.length === 0 ? (
        <EmptyState
          icon={<AlertTriangle className="h-6 w-6" />}
          title={t("empty")}
          description={
            hasFilters
              ? t("empty_filtered")
              : t("empty_hint")
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Severity</TableHead>
                <TableHead>Issue</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Tenant</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead>Resource</TableHead>
                <TableHead className="text-right">Action</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {issues.map((issue) => (
                <TableRow key={issue.id}>
                  <TableCell><SeverityBadge severity={issue.severity} /></TableCell>
                  <TableCell>
                    <div className="font-medium">{issue.title}</div>
                    <div className="font-mono text-[11px] text-muted-foreground">{issue.code}</div>
                  </TableCell>
                  <TableCell>{issue.appName ?? "-"}</TableCell>
                  <TableCell>{issue.tenantName ?? "-"}</TableCell>
                  <TableCell className="font-mono text-xs">{issue.environment ?? "-"}</TableCell>
                  <TableCell>
                    {issue.suggestedHref ? (
                      <Link href={issue.suggestedHref} className="text-primary">
                        {issue.resourceType}
                      </Link>
                    ) : (
                      <span>{issue.resourceType}</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    {issue.suggestedHref ? (
                      <Button asChild size="sm" variant="outline">
                        <Link href={issue.suggestedHref}>
                          {issue.suggestedAction}
                        </Link>
                      </Button>
                    ) : (
                      <span className="text-sm text-muted-foreground">
                        {issue.suggestedAction}
                      </span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const variant = severity === "critical" ? "destructive" : severity === "warning" ? "warning" : "outline";
  return <Badge variant={variant} className="font-mono text-[10px] uppercase">{severity}</Badge>;
}

function QuickFilter({
  active,
  children,
  href,
}: {
  active: boolean;
  children: ReactNode;
  href: string;
}) {
  return (
    <Button asChild size="sm" variant={active ? "default" : "outline"}>
      <Link href={href}>{children}</Link>
    </Button>
  );
}

function buildQuery(filters: IssueFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value);
    }
  }
  const query = params.toString();
  return query ? `?${query}` : "";
}
