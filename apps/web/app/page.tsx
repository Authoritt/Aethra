import Link from "next/link";
import { getTranslations } from "next-intl/server";
import {
  Activity,
  ArrowRight,
  BellRing,
  CheckCircle2,
  Database,
  GitBranch,
  Globe2,
  LockKeyhole,
  MonitorCheck,
  ServerCog,
  ShieldCheck,
  Workflow,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Logo } from "@/components/brand/logo";
import { StatusPill } from "@/components/ui/status-pill";

const capabilities = [
  {
    icon: GitBranch,
    title: "capabilities.deploy_title",
    description: "capabilities.deploy_description",
  },
  {
    icon: Workflow,
    title: "capabilities.tenancy_title",
    description: "capabilities.tenancy_description",
  },
  {
    icon: ServerCog,
    title: "capabilities.infrastructure_title",
    description: "capabilities.infrastructure_description",
  },
  {
    icon: Globe2,
    title: "capabilities.access_title",
    description: "capabilities.access_description",
  },
  {
    icon: Database,
    title: "capabilities.services_title",
    description: "capabilities.services_description",
  },
  {
    icon: MonitorCheck,
    title: "capabilities.observability_title",
    description: "capabilities.observability_description",
  },
] as const;

const workflowSteps = [
  "workflow.step_1",
  "workflow.step_2",
  "workflow.step_3",
  "workflow.step_4",
] as const;

const assurances = [
  {
    icon: ShieldCheck,
    title: "assurances.access_title",
    description: "assurances.access_description",
  },
  {
    icon: LockKeyhole,
    title: "assurances.secrets_title",
    description: "assurances.secrets_description",
  },
  {
    icon: BellRing,
    title: "assurances.continuity_title",
    description: "assurances.continuity_description",
  },
] as const;

export default async function Home() {
  const t = await getTranslations("pages.landing");

  return (
    <main className="min-h-screen bg-background text-foreground">
      <section className="border-b border-border bg-foreground text-background">
        <div className="mx-auto flex min-h-[86vh] max-w-7xl flex-col px-6 py-6 sm:px-8 lg:px-10">
          <header className="flex items-center justify-between gap-4">
            <Logo variant="lockup" className="text-background" />
            <Button asChild variant="secondary" size="sm">
              <Link href="/login">{t("login_cta")}</Link>
            </Button>
          </header>

          <div className="mx-auto flex w-full max-w-5xl flex-1 flex-col justify-center py-16 text-center">
            <div className="mb-5 flex justify-center">
              <Badge className="border-background/15 bg-background/10 text-background hover:bg-background/15">
                {t("eyebrow")}
              </Badge>
            </div>
            <h1 className="mx-auto max-w-4xl text-4xl font-semibold leading-[1.05] sm:text-6xl">
              {t("headline")}
            </h1>
            <p className="mx-auto mt-6 max-w-3xl text-base leading-7 text-background/72 sm:text-lg">
              {t("lead")}
            </p>
            <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
              <Button asChild size="lg" className="w-full sm:w-auto">
                <Link href="/login">
                  {t("primary_cta")}
                  <ArrowRight className="h-4 w-4" />
                </Link>
              </Button>
              <Button
                asChild
                size="lg"
                variant="outline"
                className="w-full border-background/20 bg-transparent text-background hover:bg-background hover:text-foreground sm:w-auto"
              >
                <a href="#capabilities">{t("secondary_cta")}</a>
              </Button>
            </div>
          </div>

          <ProductPreview t={t} />
        </div>
      </section>

      <section
        id="capabilities"
        className="mx-auto grid max-w-7xl gap-10 px-6 py-16 sm:px-8 lg:grid-cols-[0.95fr_1.4fr] lg:px-10 lg:py-24"
      >
        <div>
          <p className="text-sm font-medium uppercase text-primary">
            {t("capabilities_eyebrow")}
          </p>
          <h2 className="mt-3 text-3xl font-semibold leading-tight sm:text-4xl">
            {t("capabilities_headline")}
          </h2>
          <p className="mt-4 text-base leading-7 text-muted-foreground">
            {t("capabilities_description")}
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          {capabilities.map((item) => {
            const Icon = item.icon;
            return (
              <Card key={item.title} className="rounded-md shadow-none">
                <CardContent className="flex h-full flex-col gap-4 p-5">
                  <span className="flex h-10 w-10 items-center justify-center rounded-md border border-primary/20 bg-primary/10 text-primary">
                    <Icon className="h-5 w-5" />
                  </span>
                  <div>
                    <h3 className="font-semibold">{t(item.title)}</h3>
                    <p className="mt-2 text-sm leading-6 text-muted-foreground">
                      {t(item.description)}
                    </p>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      </section>

      <section className="border-y border-border bg-muted/35">
        <div className="mx-auto grid max-w-7xl gap-10 px-6 py-16 sm:px-8 lg:grid-cols-[1fr_1.25fr] lg:px-10">
          <div>
            <p className="text-sm font-medium uppercase text-primary">
              {t("workflow_eyebrow")}
            </p>
            <h2 className="mt-3 text-3xl font-semibold leading-tight sm:text-4xl">
              {t("workflow_headline")}
            </h2>
          </div>
          <ol className="grid gap-3 sm:grid-cols-2">
            {workflowSteps.map((step, index) => (
              <li
                key={step}
                className="rounded-md border border-border bg-background p-5"
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-sm font-semibold text-primary-foreground">
                  {index + 1}
                </span>
                <p className="mt-4 text-sm leading-6 text-muted-foreground">
                  {t(step)}
                </p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section className="mx-auto max-w-7xl px-6 py-16 sm:px-8 lg:px-10 lg:py-24">
        <div className="grid gap-6 lg:grid-cols-[0.8fr_1.2fr]">
          <div>
            <p className="text-sm font-medium uppercase text-primary">
              {t("assurances_eyebrow")}
            </p>
            <h2 className="mt-3 text-3xl font-semibold leading-tight sm:text-4xl">
              {t("assurances_headline")}
            </h2>
          </div>
          <div className="grid gap-4 md:grid-cols-3">
            {assurances.map((item) => {
              const Icon = item.icon;
              return (
                <div
                  key={item.title}
                  className="rounded-md border border-border p-5"
                >
                  <Icon className="h-5 w-5 text-primary" />
                  <h3 className="mt-4 font-semibold">{t(item.title)}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">
                    {t(item.description)}
                  </p>
                </div>
              );
            })}
          </div>
        </div>
      </section>

      <section className="border-t border-border">
        <div className="mx-auto flex max-w-7xl flex-col gap-5 px-6 py-8 sm:flex-row sm:items-center sm:justify-between sm:px-8 lg:px-10">
          <div>
            <p className="text-sm font-semibold">{t("final_title")}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("final_description")}
            </p>
          </div>
          <Button asChild>
            <Link href="/login">
              {t("login_cta")}
              <ArrowRight className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </section>
    </main>
  );
}

function ProductPreview({
  t,
}: {
  t: Awaited<ReturnType<typeof getTranslations<"pages.landing">>>;
}) {
  return (
    <div className="mx-auto w-full max-w-6xl overflow-hidden rounded-t-md border border-background/15 bg-background text-foreground shadow-2xl">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <span className="h-2.5 w-2.5 rounded-full bg-destructive" />
          <span className="h-2.5 w-2.5 rounded-full bg-warning" />
          <span className="h-2.5 w-2.5 rounded-full bg-success" />
        </div>
        <StatusPill variant="success">
          <CheckCircle2 className="h-3 w-3" />
          {t("preview_status")}
        </StatusPill>
      </div>

      <div className="grid min-h-[360px] md:grid-cols-[220px_1fr]">
        <aside className="hidden border-r border-border bg-muted/45 p-4 md:block">
          <p className="text-xs font-semibold uppercase text-muted-foreground">
            {t("preview_sidebar_label")}
          </p>
          <div className="mt-4 space-y-2">
            {[
              "preview_nav_projects",
              "preview_nav_builds",
              "preview_nav_routes",
              "preview_nav_services",
              "preview_nav_monitors",
            ].map((item, index) => (
              <div
                key={item}
                className={`rounded-md px-3 py-2 text-sm ${
                  index === 0
                    ? "bg-background font-medium text-foreground shadow-sm"
                    : "text-muted-foreground"
                }`}
              >
                {t(item)}
              </div>
            ))}
          </div>
        </aside>

        <div className="p-4 sm:p-6">
          <div className="flex flex-col gap-3 border-b border-border pb-5 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <p className="text-sm text-muted-foreground">
                {t("preview_project_label")}
              </p>
              <h3 className="mt-1 text-2xl font-semibold">
                {t("preview_project")}
              </h3>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge variant="secondary">{t("preview_environment")}</Badge>
              <Badge variant="outline">{t("preview_tenant")}</Badge>
            </div>
          </div>

          <div className="grid gap-4 pt-5 lg:grid-cols-[1fr_0.86fr]">
            <div className="rounded-md border border-border p-4">
              <div className="flex items-center justify-between gap-3">
                <h4 className="font-semibold">{t("preview_pipeline_title")}</h4>
                <StatusPill variant="running">{t("preview_pipeline_status")}</StatusPill>
              </div>
              <div className="mt-5 space-y-3">
                {[
                  ["success", "preview_pipeline_1"],
                  ["success", "preview_pipeline_2"],
                  ["running", "preview_pipeline_3"],
                  ["success", "preview_pipeline_4"],
                ].map(([variant, label]) => (
                  <div
                    key={label}
                    className="flex items-center justify-between rounded-md bg-muted/55 px-3 py-2 text-sm"
                  >
                    <span>{t(label)}</span>
                    <StatusPill
                      variant={variant as "success" | "running"}
                      withDot={false}
                    >
                      {variant === "running" ? t("running") : t("done")}
                    </StatusPill>
                  </div>
                ))}
              </div>
            </div>

            <div className="grid gap-4">
              {[
                ["preview_panel_routing_title", "preview_panel_routing_text", Globe2],
                ["preview_panel_services_title", "preview_panel_services_text", Database],
                ["preview_panel_context_title", "preview_panel_context_text", Activity],
              ].map(([title, text, Icon]) => {
                const PreviewIcon = Icon as typeof Globe2;
                return (
                  <div key={title as string} className="rounded-md border border-border p-4">
                    <div className="flex items-start gap-3">
                      <span className="flex h-9 w-9 items-center justify-center rounded-md bg-primary/10 text-primary">
                        <PreviewIcon className="h-4 w-4" />
                      </span>
                      <div>
                        <h4 className="text-sm font-semibold">{t(title as string)}</h4>
                        <p className="mt-1 text-xs leading-5 text-muted-foreground">
                          {t(text as string)}
                        </p>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
