import { redirect } from "next/navigation";
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
        return r.map((t) => ({
          id: t.id,
          name: t.name,
          slug: t.slug,
          projectName: p.name,
          branch: t.branch,
        }));
      }),
    );
    templates = lists.flat();
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Builds", href: "/builds" },
          { label: "Nuevo" },
        ]}
        title="Disparar build manual"
        description="Selecciona el template y commit a buildear. Para auto-deploy, usa el webhook configurado en el template."
      />

      <div className="max-w-2xl">
        {templates.length === 0 ? (
          <EmptyState
            icon={Rocket}
            title="No hay templates disponibles"
            description="Necesitás al menos un template en algún proyecto para disparar builds."
          />
        ) : projects === "error" ? (
          <Card className="border-destructive/30 bg-destructive/5">
            <CardContent className="p-4 text-sm text-destructive">
              Error cargando templates.
            </CardContent>
          </Card>
        ) : (
          <TriggerBuildForm templates={templates} />
        )}
      </div>
    </div>
  );
}
