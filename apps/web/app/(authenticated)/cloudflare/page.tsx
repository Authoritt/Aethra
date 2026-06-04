import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Cloud, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardFooter, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { CloudflareZoneDto } from "@/lib/types";
import { ZoneStatusPill } from "./ZoneStatusPill";
import { SyncZoneButton } from "./SyncZoneButton";

export const dynamic = "force-dynamic";

async function fetchZones(): Promise<
  CloudflareZoneDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/cloudflare/zones/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as CloudflareZoneDto[];
}

export default async function CloudflareZonesPage() {
  const t = await getTranslations("pages.cloudflare_list");
  const tCommon = await getTranslations("common");
  const data = await fetchZones();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const zones = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="outline">
              <Link href="/cloudflare/tunnel">
                <Cloud className="mr-2 h-4 w-4" />
                Túnel (ingress)
              </Link>
            </Button>
            <Button asChild>
              <Link href="/cloudflare/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("register_zone")}
              </Link>
            </Button>
          </div>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error")}
          </CardContent>
        </Card>
      ) : zones.length === 0 ? (
        <EmptyState
          icon={<Cloud className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/cloudflare/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("register_zone")}
              </Link>
            </Button>
          }
        />
      ) : (
        <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {zones.map((zone) => (
            <li key={zone.id}>
              <Card>
                <CardHeader className="flex-row items-start justify-between gap-3 space-y-0">
                  <div className="min-w-0">
                    <Link
                      href={`/cloudflare/${zone.id}`}
                      className="block truncate text-base font-semibold hover:text-primary"
                    >
                      {zone.name}
                    </Link>
                    <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                      {zone.external_zone_id}
                    </p>
                  </div>
                  <ZoneStatusPill status={zone.status} />
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-2 gap-3 text-xs">
                    <div>
                      <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
                        {t("label_records")}
                      </div>
                      <div className="mt-0.5 font-mono">
                        {zone.records_count}
                      </div>
                    </div>
                    <div>
                      <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
                        {t("label_last_sync")}
                      </div>
                      <div className="mt-0.5">
                        {formatRelative(zone.last_synced_at, t, tCommon)}
                      </div>
                    </div>
                  </div>
                </CardContent>
                <CardFooter className="justify-between border-t border-border pt-3">
                  <Link
                    href={`/cloudflare/${zone.id}`}
                    className="text-xs text-muted-foreground hover:text-primary"
                  >
                    {t("see_records")}
                  </Link>
                  <SyncZoneButton zoneId={zone.id} />
                </CardFooter>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// next-intl `getTranslations` returns a function whose `values` param is typed
// as `Record<string, string|number|Date>`. We accept that shape directly.
type Translator = (
  key: string,
  values?: Record<string, string | number | Date>,
) => string;

function formatRelative(
  iso: string | null,
  t: Translator,
  tCommon: Translator,
): string {
  if (!iso) return tCommon("never");
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return t("relative_seconds_ago");
  if (minutes < 60) return t("relative_minutes_ago", { minutes });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return t("relative_hours_ago", { hours });
  const days = Math.floor(hours / 24);
  return t("relative_days_ago", { days });
}
