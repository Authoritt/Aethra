import Link from "next/link";
import { redirect } from "next/navigation";
import { Plus, Rocket } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { BuildStatusPill } from "@/components/aethra/build-status-pill";
import { serverFetch } from "@/lib/server-fetch";
import type {
  BuildSummary,
  ProjectSummaryV2,
  TemplateSummary,
} from "@/lib/types";

export const dynamic = "force-dynamic";

interface AggregatedBuild extends BuildSummary {
  templateName: string;
  templateSlug: string;
}

async function aggregateRecentBuilds(): Promise<
  AggregatedBuild[] | "unauthorized" | "error"
> {
  const projects = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (projects === "unauthorized") return "unauthorized";
  if (projects === "error") return "error";
  if (!Array.isArray(projects) || projects.length === 0) return [];

  const templateLists = await Promise.all(
    projects.map((p) =>
      serverFetch<TemplateSummary[]>(`/api/projects/${p.id}/templates`),
    ),
  );
  const templates: TemplateSummary[] = templateLists
    .filter((t): t is TemplateSummary[] => Array.isArray(t))
    .flat();

  if (templates.length === 0) return [];

  const buildLists = await Promise.all(
    templates.map(async (t) => {
      const builds = await serverFetch<BuildSummary[]>(
        `/api/builds/templates/${t.id}`,
      );
      if (!Array.isArray(builds)) return [] as AggregatedBuild[];
      return builds.map((b) => ({
        ...b,
        templateName: t.name,
        templateSlug: t.slug,
      }));
    }),
  );

  const merged = buildLists.flat();
  merged.sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
  return merged.slice(0, 50);
}

export default async function BuildsPage() {
  const data = await aggregateRecentBuilds();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const builds = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Builds"
        description="Últimos 50 builds agregados de todos los templates del workspace."
        actions={
          <Button asChild>
            <Link href="/builds/new">
              <Plus className="mr-2 h-4 w-4" />
              Build manual
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado. Verificá que la API esté corriendo.
          </CardContent>
        </Card>
      ) : builds.length === 0 ? (
        <EmptyState
          icon={Rocket}
          title="Sin builds aún"
          description="Cuando dispares un webhook o un build manual desde un template, los últimos aparecerán aquí."
          action={
            <Button asChild>
              <Link href="/builds/new">
                <Plus className="mr-2 h-4 w-4" />
                Disparar build manual
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Status</TableHead>
                <TableHead>Template</TableHead>
                <TableHead>Ref</TableHead>
                <TableHead>SHA</TableHead>
                <TableHead>Trigger</TableHead>
                <TableHead>Creado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {builds.map((b) => (
                <TableRow key={b.id}>
                  <TableCell>
                    <Link href={`/builds/${b.id}`}>
                      <BuildStatusPill status={b.status} />
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Link
                      href={`/templates/${b.templateId}`}
                      className="text-sm hover:text-primary"
                    >
                      {b.templateName}
                    </Link>
                    <div className="font-mono text-[10px] text-muted-foreground">
                      {b.templateSlug}
                    </div>
                  </TableCell>
                  <TableCell className="font-mono text-xs">{b.gitRef}</TableCell>
                  <TableCell className="font-mono text-[11px] text-muted-foreground">
                    {b.gitSha.slice(0, 8)}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" className="text-xs font-normal">
                      {b.trigger}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatDate(b.createdAt)}
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
