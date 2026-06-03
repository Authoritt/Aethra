import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { FolderKanban, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { ProjectSummaryV2 } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ProjectsPage() {
  const data = await serverFetch<ProjectSummaryV2[]>("/api/projects");
  if (data === "unauthorized") redirect("/login");

  const t = await getTranslations("pages.projects");
  const projects = Array.isArray(data) ? data : [];
  const errored = data === "error";

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Button asChild>
            <Link href="/projects/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("new_project")}
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      ) : projects.length === 0 ? (
        <EmptyState
          icon={<FolderKanban className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/projects/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("create_project")}
              </Link>
            </Button>
          }
        />
      ) : (
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {projects.map((p) => (
            <li key={p.id}>
              <Link href={`/projects/${p.id}`} className="group block h-full">
                <Card className="h-full transition-colors group-hover:border-primary/40">
                  <CardContent className="space-y-2 p-5">
                    <div className="flex items-start justify-between gap-2">
                      <h3 className="truncate text-base font-semibold text-foreground">
                        {p.name}
                      </h3>
                      {p.color ? (
                        <span
                          className="mt-1 size-3 shrink-0 rounded-full ring-1 ring-border"
                          style={{ backgroundColor: p.color }}
                          aria-hidden
                        />
                      ) : null}
                    </div>
                    <p className="font-mono text-xs text-muted-foreground">
                      {p.slug}
                    </p>
                    {p.icon ? (
                      <p className="inline-flex items-center gap-1 rounded-md border border-border bg-muted px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                        {t("icon_label", { value: p.icon })}
                      </p>
                    ) : null}
                  </CardContent>
                </Card>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
