import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Plug2, Plus, RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { IntegrationCredentialDto } from "@/lib/types";
import { DeleteIntegrationButton } from "./DeleteIntegrationButton";

export const dynamic = "force-dynamic";

async function fetchCredentials(): Promise<
  IntegrationCredentialDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/integrations/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as IntegrationCredentialDto[];
}

export default async function IntegrationsPage() {
  const t = await getTranslations("pages.settings_integrations");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchCredentials();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const creds = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tSettings("title"), href: "/settings" },
          { label: t("breadcrumb") },
        ]}
        title={t("list_title")}
        description={
          <>
            {t("list_description_prefix")}
            <span className="font-mono">{t("list_description_field")}</span>
            {t("list_description_format_prefix")}
            <span className="font-mono">{t("list_description_format_value")}</span>
            {t("list_description_format_suffix")}
          </>
        }
        actions={
          <Button asChild>
            <Link href="/settings/integrations/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("list_action_new")}
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : creds.length === 0 ? (
        <EmptyState
          icon={<Plug2 className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/settings/integrations/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("list_action_new")}
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("col_name")}</TableHead>
                <TableHead>{t("col_type")}</TableHead>
                <TableHead>{t("col_display")}</TableHead>
                <TableHead>{t("col_created")}</TableHead>
                <TableHead>{t("col_last_rotation")}</TableHead>
                <TableHead>{t("col_last_use")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {creds.map((c) => (
                <TableRow key={c.id}>
                  <TableCell className="align-top">
                    <Badge
                      variant="outline"
                      className="font-mono text-[11px]"
                    >
                      {c.name}
                    </Badge>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {c.id}
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className="font-mono text-[10px] uppercase">
                      {c.type}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="text-foreground">{c.displayName}</div>
                    {c.description ? (
                      <div className="mt-0.5 text-xs text-muted-foreground">
                        {c.description}
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(c.createdAt)}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.rotatedAt, {
                      never: t("relative_never"),
                      seconds: t("relative_seconds_ago"),
                      minutes: (m) => t("relative_minutes_ago", { minutes: m }),
                      hours: (h) => t("relative_hours_ago", { hours: h }),
                      days: (d) => t("relative_days_ago", { days: d }),
                    })}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.lastUsedAt, {
                      never: t("relative_never"),
                      seconds: t("relative_seconds_ago"),
                      minutes: (m) => t("relative_minutes_ago", { minutes: m }),
                      hours: (h) => t("relative_hours_ago", { hours: h }),
                      days: (d) => t("relative_days_ago", { days: d }),
                    })}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      <Button asChild variant="outline" size="sm">
                        <Link
                          href={`/settings/integrations/${encodeURIComponent(c.id)}/rotate`}
                        >
                          <RefreshCw className="mr-2 h-4 w-4" />
                          {t("action_rotate")}
                        </Link>
                      </Button>
                      <DeleteIntegrationButton id={c.id} name={c.name} />
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <Card className="mt-6">
        <CardContent className="p-5 text-sm text-muted-foreground">
          <h3 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {t("info_title")}
          </h3>
          <p className="mt-2">
            {t("info_inject_prefix")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_resolver")}
            </code>
            {t("info_and_call")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_get_secret")}
            </code>
            {t("info_suffix")}
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}

interface RelativeStrings {
  never: string;
  seconds: string;
  minutes: (n: number) => string;
  hours: (n: number) => string;
  days: (n: number) => string;
}

function formatRelative(iso: string | null | undefined, s: RelativeStrings): string {
  if (!iso) return s.never;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return s.seconds;
  if (minutes < 60) return s.minutes(minutes);
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return s.hours(hours);
  const days = Math.floor(hours / 24);
  return s.days(days);
}
