import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ProjectDetailV2 } from "@/lib/types";
import { NewClientForm } from "./NewClientForm";

export const dynamic = "force-dynamic";

export default async function NewClientPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const t = await getTranslations("pages.clients_new");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const tProjects = await getTranslations("pages.projects_detail");
  const { projectId } = await params;
  const project = await serverFetch<ProjectDetailV2>(
    `/api/projects/${projectId}`,
  );
  if (project === "unauthorized") redirect("/login");
  if (project === "notfound") notFound();
  if (project === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tProjects("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("projects"), href: "/projects" },
          { label: project.name, href: `/projects/${project.id}` },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <div className="max-w-2xl">
        <NewClientForm projectId={project.id} />
      </div>
    </div>
  );
}
