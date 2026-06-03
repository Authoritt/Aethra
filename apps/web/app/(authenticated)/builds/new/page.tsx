import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { Rocket } from "lucide-react";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";
import { TriggerBuildForm, type TemplateOption } from "./TriggerBuildForm";

export const dynamic = "force-dynamic";

export default async function NewBuildPage() {
  const t = await getTranslations("pages.builds_new");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") redirect("/login");

  let templates: TemplateOption[] = [];
  if (Array.isArray(projects) && projects.length > 0) {
    const lists = await Promise.all(
      projects.map(async (p) => {
        const r = await serverFetch<TemplateSummary[]>(
          `/api/projects/${p.id}/templates`,
        );
        if (!Array.isArray(r)) return [];
        return r.map((tpl) => ({
          id: tpl.id,
          name: tpl.name,
          slug: tpl.slug,
          projectName: p.name,
          branch: tpl.branch,
        }));
      }),
    );
    templates = lists.flat();
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("builds"), href: "/builds" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />

      <div className="max-w-2xl">
        {templates.length === 0 ? (
          <EmptyState
            icon={<Rocket className="h-6 w-6" />}
            title={t("placeholder_template")}
            description={t("description")}
          />
        ) : projects === "error" ? (
          <Card className="border-destructive/30 bg-destructive/5">
            <CardContent className="p-4 text-sm text-destructive">
              {t("error_unknown")}
            </CardContent>
          </Card>
        ) : (
          <TriggerBuildForm templates={templates} />
        )}
      </div>
    </div>
  );
}
