import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Plus, UserIcon, Users as UsersIcon } from "lucide-react";
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
import type { UserSummary } from "@/lib/types";
import { UserRowActions } from "./UserRowActions";

export const dynamic = "force-dynamic";

async function fetchUsers(): Promise<UserSummary[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/users`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 403) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as UserSummary[];
}

export default async function UsersPage() {
  const t = await getTranslations("pages.settings_users");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchUsers();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const users = Array.isArray(data) ? data : [];

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
          <div className="flex gap-2">
            <Button asChild variant="outline">
              <Link href="/settings/roles">{t("list_action_roles")}</Link>
            </Button>
            <Button asChild>
              <Link href="/settings/users/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("list_action_new")}
              </Link>
            </Button>
          </div>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : users.length === 0 ? (
        <EmptyState
          icon={<UsersIcon className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/settings/users/new">
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
                <TableHead>{t("col_user")}</TableHead>
                <TableHead>{t("col_roles")}</TableHead>
                <TableHead>{t("col_last_login")}</TableHead>
                <TableHead>{t("col_status")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((u) => (
                <TableRow key={u.id}>
                  <TableCell className="align-top">
                    <div className="flex items-center gap-2">
                      <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">
                        <UserIcon className="h-4 w-4" />
                      </div>
                      <div className="min-w-0">
                        <div className="truncate font-medium">
                          {u.displayName ?? u.email}
                        </div>
                        <div className="truncate font-mono text-[10px] text-muted-foreground">
                          {u.email}
                        </div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex flex-wrap gap-1">
                      {u.roles.length === 0 ? (
                        <Badge variant="outline" className="text-[10px]">
                          {t("no_roles")}
                        </Badge>
                      ) : (
                        u.roles.map((r) => (
                          <Badge
                            key={r.id}
                            variant={
                              r.slug === "admin" ? "warning" : "outline"
                            }
                            className="text-[10px]"
                          >
                            {r.displayName}
                          </Badge>
                        ))
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(u.lastLoginAt, {
                      never: t("relative_never"),
                      seconds: t("relative_seconds_ago"),
                      minutes: (m) => t("relative_minutes_ago", { minutes: m }),
                      hours: (h) => t("relative_hours_ago", { hours: h }),
                      days: (d) => t("relative_days_ago", { days: d }),
                    })}
                  </TableCell>
                  <TableCell className="align-top">
                    {u.isActive ? (
                      <Badge variant="success" className="text-[10px]">
                        {t("status_active")}
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-[10px]">
                        {t("status_inactive")}
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <UserRowActions
                      id={u.id}
                      email={u.email}
                      isActive={u.isActive}
                    />
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
