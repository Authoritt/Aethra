import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { MonitorCheckDto, MonitorDetailDto } from "@/lib/types";
import MonitorLatencyChart from "../MonitorLatencyChart";
import { CheckHistoryTable } from "../CheckHistoryTable";
import { TriggerCheckButton } from "../TriggerCheckButton";
import { EnableDisableButtons } from "../EnableDisableButtons";
import { DeleteMonitorButton } from "../DeleteMonitorButton";
import MonitorDetailLive from "../MonitorDetailLive";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchMonitor(
  monitorId: string,
): Promise<MonitorDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/monitors/${monitorId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as MonitorDetailDto;
}

async function fetchChecks(monitorId: string): Promise<MonitorCheckDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(
    `${API_URL}/api/monitors/${monitorId}/checks?limit=100`,
    { headers: { cookie: cookieHeader }, cache: "no-store" },
  );
  if (!res.ok) return [];
  return (await res.json()) as MonitorCheckDto[];
}

export default async function MonitorDetailPage({
  params,
}: {
  params: Promise<{ monitorId: string }>;
}) {
  const t = await getTranslations("pages.monitors_detail");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { monitorId } = await params;
  const data = await fetchMonitor(monitorId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }

  const monitor = data;
  const checks = await fetchChecks(monitorId);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("monitors"), href: "/monitors" },
          { label: monitor.name },
        ]}
        title={monitor.name}
        description={
          <>
            <span className="font-mono text-xs">{monitor.slug}</span>
            <span className="mx-2 text-muted-foreground/50">·</span>
            <span className="font-mono">
              {monitor.http_method} {monitor.url}
            </span>
          </>
        }
        actions={
          <>
            <TriggerCheckButton monitorId={monitor.id} />
            <EnableDisableButtons
              monitorId={monitor.id}
              isEnabled={monitor.is_enabled}
            />
            <Button asChild variant="outline" size="sm">
              <Link href={`/monitors/${monitor.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                {t("edit")}
              </Link>
            </Button>
            <DeleteMonitorButton monitorId={monitor.id} name={monitor.name} />
          </>
        }
      />

      <div className="mb-6">
        <MonitorDetailLive
          monitorId={monitor.id}
          initialStatus={monitor.status}
          initialLastCheckedAt={monitor.last_checked_at}
          isEnabled={monitor.is_enabled}
        />
      </div>

      <section className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
        <InfoCard label={t("label_interval")} value={`${monitor.interval_sec}s`} />
        <InfoCard label={t("label_timeout")} value={`${monitor.timeout_ms}ms`} />
        <InfoCard
          label={t("label_expected_ok")}
          value={monitor.expected_status_codes.join(", ")}
          mono
        />
        <InfoCard
          label={t("label_failures")}
          value={String(monitor.consecutive_failures)}
          mono
        />
      </section>

      <section className="mb-6">
        <h2 className="mb-3 text-sm font-medium uppercase tracking-wider text-muted-foreground">
          {t("latency_title", { count: checks.length })}
        </h2>
        <MonitorLatencyChart checks={checks} />
      </section>

      {monitor.headers || monitor.body_template ? (
        <Card className="mb-6">
          <CardContent className="p-5">
            <h2 className="mb-3 text-sm font-medium uppercase tracking-wider text-muted-foreground">
              {t("request_title")}
            </h2>
            {monitor.headers && Object.keys(monitor.headers).length > 0 ? (
              <div className="mb-3">
                <h3 className="text-xs font-medium uppercase text-muted-foreground">
                  {t("headers_title")}
                </h3>
                <dl className="mt-1 grid grid-cols-1 gap-1 font-mono text-xs">
                  {Object.entries(monitor.headers).map(([k, v]) => (
                    <div key={k} className="flex gap-2">
                      <dt className="text-muted-foreground">{k}:</dt>
                      <dd className="break-all text-foreground">{v}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            ) : null}
            {monitor.body_template ? (
              <div>
                <h3 className="text-xs font-medium uppercase text-muted-foreground">
                  {t("body_title")}
                </h3>
                <pre className="mt-1 whitespace-pre-wrap break-all rounded-md border border-border bg-muted px-3 py-2 font-mono text-xs text-foreground">
                  {monitor.body_template}
                </pre>
              </div>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <section>
        <h2 className="mb-3 text-sm font-medium uppercase tracking-wider text-muted-foreground">
          {t("history_title")}
        </h2>
        <CheckHistoryTable checks={checks} />
      </section>
    </div>
  );
}

function InfoCard({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
          {label}
        </div>
        <div
          className={`mt-1 text-sm text-foreground ${mono ? "font-mono" : ""}`}
        >
          {value}
        </div>
      </CardContent>
    </Card>
  );
}
