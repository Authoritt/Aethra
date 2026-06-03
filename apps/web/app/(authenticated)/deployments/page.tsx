import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Activity, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { DeploymentStatusPill } from "@/components/aethra/deployment-status-pill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ClientSummary,
  DeploymentSummary,
  InstanceSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedDeployment extends DeploymentSummary {
  instanceSlug: string;
  templateName: string;
  templateId: string;
  clientDisplayName: string;
  environment: string;
}

async function aggregate(searchParams: {
  instanceId?: string;
  status?: string;
}): Promise<AggregatedDeployment[] | "unauthorized" | "error"> {
  if (searchParams.instanceId) {
    const direct = await serverFetch<DeploymentSummary[]>(
      `/api/deployments/instances/${searchParams.instanceId}`,
    );
    if (direct === "unauthorized") return "unauthorized";
    if (direct === "error") return "error";
    if (!Array.isArray(direct)) return [];
    return direct.slice(0, 50).map((d) => ({
      ...d,
      instanceSlug: searchParams.instanceId!.slice(0, 8),
      templateName: "—",
      templateId: "",
      clientDisplayName: "—",
      environment: "—",
    }));
  }

  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const [templateLists, clientLists] = await Promise.all([
    Promise.all(
      projects.map((p) =>
        serverFetch<TemplateSummary[]>(`/api/projects/${p.id}/templates`),
      ),
    ),
    Promise.all(
      projects.map((p) =>
        serverFetch<ClientSummary[]>(`/api/projects/${p.id}/clients`),
      ),
    ),
  ]);
  const templates = templateLists
    .filter((t): t is TemplateSummary[] => Array.isArray(t))
    .flat();
  const clientsById = new Map<string, ClientSummary>();
  for (const list of clientLists) {
    if (Array.isArray(list)) {
      for (const c of list) clientsById.set(c.id, c);
    }
  }

  const templatesById = new Map(templates.map((t) => [t.id, t]));

  const instanceLists = await Promise.all(
    templates.map((t) =>
      serverFetch<InstanceSummary[]>(`/api/templates/${t.id}/instances`),
    ),
  );
  const instances: InstanceSummary[] = instanceLists
    .filter((i): i is InstanceSummary[] => Array.isArray(i))
    .flat();

  if (instances.length === 0) return [];

  const deployLists = await Promise.all(
    instances.map(async (inst) => {
      const list = await serverFetch<DeploymentSummary[]>(
        `/api/deployments/instances/${inst.id}`,
      );
      if (!Array.isArray(list)) return [] as AggregatedDeployment[];
      const tpl = templatesById.get(inst.templateId);
      const client = clientsById.get(inst.clientId);
      return list.map((d) => ({
        ...d,
        instanceSlug: inst.slug,
        templateName: tpl?.name ?? "—",
        templateId: inst.templateId,
        clientDisplayName: client?.displayName ?? "—",
        environment: inst.environment,
      }));
    }),
  );
  let merged = deployLists.flat();
  merged.sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
  if (searchParams.status) {
    const wanted = searchParams.status.toLowerCase();
    merged = merged.filter((d) => d.status.toLowerCase() === wanted);
  }
  return merged.slice(0, 50);
}

export default async function DeploymentsPage({
  searchParams,
}: {
  searchParams: Promise<{ instanceId?: string; status?: string }>;
}) {
  const t = await getTranslations("pages.deployments_list");
  const tCommon = await getTranslations("common");
  const sp = await searchParams;
  const data = await aggregate(sp);
  if (data === "unauthorized") redirect("/login");

  const filterStatus = sp.status ?? "";
  const filterInstance = sp.instanceId ?? "";
  const errored = data === "error";
  const deployments = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
      />

      <Card className="mb-4">
        <CardContent className="flex flex-wrap items-end gap-3 p-4">
          <form
            method="get"
            className="flex flex-wrap items-end gap-3"
          >
            <div className="space-y-1">
              <Label htmlFor="instanceId">{t("label_instance_id")}</Label>
              <Input
                id="instanceId"
                name="instanceId"
                defaultValue={filterInstance}
                placeholder="uuid"
                className="w-56 font-mono text-xs"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="status">{t("label_status")}</Label>
              <select
                id="status"
                name="status"
                defaultValue={filterStatus}
                className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <option value="">{t("all")}</option>
                <option value="Pending">Pending</option>
                <option value="Running">Running</option>
                <option value="Completed">Completed</option>
                <option value="Failed">Failed</option>
                <option value="Cancelled">Cancelled</option>
              </select>
            </div>
            <Button type="submit">{t("filter")}</Button>
            {filterStatus || filterInstance ? (
              <Button asChild variant="ghost" size="sm">
                <Link href="/deployments">
                  <X className="mr-2 h-4 w-4" />
                  {t("clear")}
                </Link>
              </Button>
            ) : null}
          </form>
        </CardContent>
      </Card>

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : deployments.length === 0 ? (
        <EmptyState
          icon={<Activity className="h-6 w-6" />}
          title={t("empty_title")}
          description={
            filterInstance || filterStatus
              ? t("empty_description_filtered")
              : t("empty_description_none")
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("col_status")}</TableHead>
                <TableHead>{t("col_instance")}</TableHead>
                <TableHead>{t("col_template")}</TableHead>
                <TableHead>{t("col_client")}</TableHead>
                <TableHead>{t("col_env")}</TableHead>
                <TableHead>{t("col_trigger")}</TableHead>
                <TableHead>{t("col_created")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {deployments.map((d) => (
                <TableRow key={d.id}>
                  <TableCell>
                    <Link href={`/deployments/${d.id}`}>
                      <DeploymentStatusPill status={d.status} />
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Link
                      href={`/instances/${d.instanceId}`}
                      className="font-mono text-xs hover:text-primary"
                    >
                      {d.instanceSlug}
                    </Link>
                  </TableCell>
                  <TableCell className="text-sm">
                    {d.templateId ? (
                      <Link
                        href={`/templates/${d.templateId}`}
                        className="hover:text-primary"
                      >
                        {d.templateName}
                      </Link>
                    ) : (
                      d.templateName
                    )}
                  </TableCell>
                  <TableCell className="text-sm">
                    {d.clientDisplayName}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" className="text-[10px] uppercase">
                      {d.environment}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {d.trigger}
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatDate(d.createdAt)}
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

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
