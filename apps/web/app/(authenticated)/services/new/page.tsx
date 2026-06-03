import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { ServiceTemplateDto, VmDto } from "@/lib/types";
import { TemplatePicker } from "./TemplatePicker";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchTemplates(): Promise<
  ServiceTemplateDto[] | "unauthorized" | "error"
> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/templates`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ServiceTemplateDto[];
}

async function fetchVms(): Promise<VmDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/vms/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return [];
  return (await res.json()) as VmDto[];
}

export default async function NewServicePage() {
  const t = await getTranslations("pages.services_new");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const templates = await fetchTemplates();
  if (templates === "unauthorized") redirect("/login");

  const vms = await fetchVms();

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("services"), href: "/services" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />

      {templates === "error" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("error_unknown")}
          </CardContent>
        </Card>
      ) : Array.isArray(templates) && templates.length === 0 ? (
        <EmptyState title={t("title")} />
      ) : Array.isArray(templates) ? (
        <TemplatePicker templates={templates} vms={vms} />
      ) : null}
    </div>
  );
}
