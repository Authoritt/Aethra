import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ClientSummary,
  EnvironmentDefinitionDto,
  TemplateDetail,
  VmDto,
} from "@/lib/types";
import { NewInstanceForm } from "./NewInstanceForm";

export const dynamic = "force-dynamic";

export default async function NewInstancePage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const t = await getTranslations("pages.instances_new");
  const tTemplates = await getTranslations("pages.templates_detail");
  const tCommon = await getTranslations("common");
  const { templateId } = await params;

  const templateResult = await serverFetch<TemplateDetail>(
    `/api/templates/${templateId}`,
  );
  if (templateResult === "unauthorized") redirect("/login");
  if (templateResult === "notfound") notFound();
  if (templateResult === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tTemplates("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }
  const template = templateResult;

  const [clientsResult, environmentsResult, vmsResult] = await Promise.all([
    serverFetch<ClientSummary[]>(`/api/projects/${template.projectId}/clients`),
    serverFetch<EnvironmentDefinitionDto[]>(`/api/settings/environments/`),
    serverFetch<VmDto[]>(`/api/vms/`),
  ]);

  const clients = Array.isArray(clientsResult) ? clientsResult : [];
  const environments = Array.isArray(environmentsResult)
    ? [...environmentsResult].sort((a, b) => a.order - b.order)
    : [];
  const vms = Array.isArray(vmsResult) ? vmsResult : [];

  const hasCatalogError =
    clientsResult === "error" ||
    environmentsResult === "error" ||
    vmsResult === "error";

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: template.name, href: `/templates/${template.id}` },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />

      {hasCatalogError ? (
        <Card className="mb-4 border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : null}

      <div className="max-w-3xl">
        <NewInstanceForm
          templateId={template.id}
          clients={clients}
          environments={environments}
          vms={vms}
        />
      </div>
    </div>
  );
}
