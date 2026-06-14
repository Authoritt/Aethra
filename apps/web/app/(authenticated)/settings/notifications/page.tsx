import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Bell, Plus, History } from "lucide-react";
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
import type { NotificationChannelDto } from "@/lib/types";
import { TestChannelButton } from "./TestChannelButton";
import { DeleteChannelButton } from "./DeleteChannelButton";
import { ToggleActiveSwitch } from "./ToggleActiveSwitch";
import { EditEventsButton } from "./EditEventsButton";

export const dynamic = "force-dynamic";

async function fetchChannels(): Promise<
  NotificationChannelDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/notifications/channels/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as NotificationChannelDto[];
}

export default async function NotificationsPage() {
  const t = await getTranslations("pages.settings_notifications");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchChannels();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const channels = Array.isArray(data) ? data : [];

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
          <div className="flex items-center gap-2">
            <Button asChild variant="outline">
              <Link href="/settings/notifications/deliveries">
                <History className="mr-2 h-4 w-4" />
                {t("list_action_history")}
              </Link>
            </Button>
            <Button asChild>
              <Link href="/settings/notifications/new">
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
      ) : channels.length === 0 ? (
        <EmptyState
          icon={<Bell className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/settings/notifications/new">
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
                <TableHead>{t("col_events")}</TableHead>
                <TableHead>{t("col_active")}</TableHead>
                <TableHead>{t("col_last_sent")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {channels.map((c) => (
                <TableRow key={c.id}>
                  <TableCell className="align-top">
                    <div className="font-medium">{c.name}</div>
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
                    {c.eventFilters.length === 0 ? (
                      <Badge variant="outline">{t("events_all")}</Badge>
                    ) : (
                      <div className="flex flex-wrap gap-1">
                        {c.eventFilters.map((e) => (
                          <Badge key={e} variant="outline" className="font-mono text-[10px]">
                            {e}
                          </Badge>
                        ))}
                      </div>
                    )}
                  </TableCell>
                  <TableCell className="align-top">
                    <ToggleActiveSwitch id={c.id} isActive={c.isActive} />
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(c.lastDeliveredAt, {
                      never: t("relative_never"),
                      seconds: t("relative_seconds_ago"),
                      minutes: (m) => t("relative_minutes_ago", { minutes: m }),
                      hours: (h) => t("relative_hours_ago", { hours: h }),
                      days: (d) => t("relative_days_ago", { days: d }),
                    })}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <div className="inline-flex items-center gap-2">
                      <EditEventsButton
                        id={c.id}
                        name={c.name}
                        eventFilters={c.eventFilters}
                      />
                      <TestChannelButton id={c.id} name={c.name} />
                      <DeleteChannelButton id={c.id} name={c.name} />
                    </div>
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
