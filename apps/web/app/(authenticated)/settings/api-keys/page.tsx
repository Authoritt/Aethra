import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Key, Plus } from "lucide-react";
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
import type { ApiKeySummary } from "@/lib/types";
import { ApiKeyStatusPill, deriveStatus } from "./ApiKeyStatusPill";
import { RevokeKeyButton } from "./RevokeKeyButton";

export const dynamic = "force-dynamic";

async function fetchKeys(): Promise<
  ApiKeySummary[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/api-keys`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ApiKeySummary[];
}

export default async function ApiKeysPage() {
  const t = await getTranslations("pages.settings_api_keys");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchKeys();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const keys = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tSettings("title"), href: "/settings" },
          { label: t("list_breadcrumb") },
        ]}
        title={t("list_title")}
        description={t("list_description")}
        actions={
          <Button asChild>
            <Link href="/settings/api-keys/new">
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
      ) : keys.length === 0 ? (
        <EmptyState
          icon={<Key className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/settings/api-keys/new">
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
                <TableHead>{t("col_prefix")}</TableHead>
                <TableHead>{t("col_scopes")}</TableHead>
                <TableHead>{t("col_created")}</TableHead>
                <TableHead>{t("col_last_used")}</TableHead>
                <TableHead>{t("col_expires")}</TableHead>
                <TableHead>{t("col_status")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((key) => {
                const status = deriveStatus(key);
                return (
                  <TableRow key={key.id}>
                    <TableCell className="align-top">
                      <div className="font-medium">{key.name}</div>
                      <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                        {key.id}
                      </div>
                    </TableCell>
                    <TableCell className="align-top">
                      <Badge
                        variant="outline"
                        className="font-mono text-[11px]"
                      >
                        {key.keyPrefix}…
                      </Badge>
                    </TableCell>
                    <TableCell className="align-top">
                      <ScopesPills
                        scopes={key.scopes}
                        labelNone={t("scope_none")}
                        labelAdmin={t("scope_admin")}
                      />
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatDate(key.createdAt)}
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatRelative(key.lastUsedAt, {
                        never: t("relative_never"),
                        seconds: t("relative_seconds_ago"),
                        minutes: (m) => t("relative_minutes_ago", { minutes: m }),
                        hours: (h) => t("relative_hours_ago", { hours: h }),
                        days: (d) => t("relative_days_ago", { days: d }),
                      })}
                    </TableCell>
                    <TableCell className="align-top text-xs text-muted-foreground">
                      {formatExpires(key.expiresAt, t("relative_never"))}
                    </TableCell>
                    <TableCell className="align-top">
                      <ApiKeyStatusPill status={status} />
                    </TableCell>
                    <TableCell className="align-top text-right">
                      <RevokeKeyButton
                        id={key.id}
                        name={key.name}
                        alreadyRevoked={status === "revoked"}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
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
            {t("info_prefix")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_bearer")}
            </code>
            {t("info_or")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_xapikey")}
            </code>
            {t("info_suffix")}
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

interface ScopesPillsProps {
  scopes: string[];
  labelNone: string;
  labelAdmin: string;
}

function ScopesPills({ scopes, labelNone, labelAdmin }: ScopesPillsProps) {
  if (scopes.length === 0) {
    return <span className="text-xs text-muted-foreground">{labelNone}</span>;
  }
  if (scopes.includes("*")) {
    return <Badge variant="warning">{labelAdmin}</Badge>;
  }
  const max = 4;
  const visible = scopes.slice(0, max);
  const overflow = scopes.length - max;
  return (
    <div className="flex flex-wrap gap-1">
      {visible.map((s) => (
        <Badge key={s} variant="outline" className="font-mono text-[10px]">
          {s}
        </Badge>
      ))}
      {overflow > 0 ? (
        <Badge variant="outline" className="font-mono text-[10px]">
          +{overflow}
        </Badge>
      ) : null}
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

function formatExpires(iso: string | null | undefined, never: string): string {
  if (!iso) return never;
  return formatDate(iso);
}
