import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { EnvironmentDefinitionDto } from "@/lib/types";
import { EnvironmentsManager } from "./EnvironmentsManager";

export const dynamic = "force-dynamic";

async function fetchEnvironments(): Promise<
  EnvironmentDefinitionDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/environments/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as EnvironmentDefinitionDto[];
}

export default async function EnvironmentsPage() {
  const t = await getTranslations("pages.settings_environments");
  const tSettings = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const data = await fetchEnvironments();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tSettings("title"), href: "/settings" },
          { label: t("list_breadcrumb") },
        ]}
        title={t("title")}
        description={t("list_description")}
      />

      {data === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : Array.isArray(data) ? (
        <EnvironmentsManager initial={data} />
      ) : null}

      <Card className="mt-6">
        <CardContent className="p-5 text-sm text-muted-foreground">
          <h3 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {t("info_title")}
          </h3>
          <p className="mt-2">
            {t("info_prefix")}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground">
              {t("info_catalog")}
            </code>
            {t("info_suffix")}
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
