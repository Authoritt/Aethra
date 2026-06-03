import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Globe, Plus } from "lucide-react";
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
import type { BaseDomainDto } from "@/lib/types";
import { ActivateBaseDomainButton } from "./ActivateBaseDomainButton";
import { MarkWildcardButton } from "./MarkWildcardButton";
import { DeleteBaseDomainButton } from "./DeleteBaseDomainButton";

export const dynamic = "force-dynamic";

async function fetchDomains(): Promise<
  BaseDomainDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/domains/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as BaseDomainDto[];
}

export default async function BaseDomainsPage() {
  const t = await getTranslations("pages.settings_domains");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchDomains();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const domains = Array.isArray(data) ? data : [];

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
            <Link href="/settings/domains/new">
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
      ) : domains.length === 0 ? (
        <EmptyState
          icon={<Globe className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/settings/domains/new">
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
                <TableHead>{t("col_hostname")}</TableHead>
                <TableHead>{t("col_zone")}</TableHead>
                <TableHead>{t("col_wildcard")}</TableHead>
                <TableHead>{t("col_status")}</TableHead>
                <TableHead>{t("col_created")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {domains.map((d) => (
                <TableRow key={d.id}>
                  <TableCell className="align-top">
                    <span className="font-mono text-xs">{d.hostname}</span>
                    <div className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                      {d.id}
                    </div>
                  </TableCell>
                  <TableCell className="align-top text-xs">
                    {d.cloudflareZoneId ? (
                      <Link
                        href={`/cloudflare/${encodeURIComponent(d.cloudflareZoneId)}`}
                        className="font-mono text-foreground hover:text-primary"
                      >
                        {d.cloudflareZoneId}
                      </Link>
                    ) : (
                      <span className="text-muted-foreground">{t("zone_unlinked")}</span>
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    {d.wildcardConfigured ? (
                      <Badge variant="success">{t("wildcard_confirmed")}</Badge>
                    ) : (
                      <MarkWildcardButton id={d.id} />
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    {d.isActive ? (
                      <Badge variant="success">{t("status_active")}</Badge>
                    ) : (
                      <Badge variant="outline">{t("status_inactive")}</Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatDate(d.createdAt)}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      {!d.isActive ? (
                        <ActivateBaseDomainButton
                          id={d.id}
                          hostname={d.hostname}
                        />
                      ) : null}
                      <DeleteBaseDomainButton
                        id={d.id}
                        hostname={d.hostname}
                        isActive={d.isActive}
                      />
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
            {t("info_prefix")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_record")}
            </code>
            {t("info_suffix_prefix")}
            <em>{t("info_suffix_em")}</em>
            {t("info_suffix_end")}
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
