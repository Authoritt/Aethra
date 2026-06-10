import Link from "next/link";
import { redirect } from "next/navigation";
import { Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { GlobalSearchResultDto } from "@/lib/types";

export const dynamic = "force-dynamic";

interface SearchParams {
  q?: string;
}

export default async function SearchPage({
  searchParams,
}: {
  searchParams: Promise<SearchParams>;
}) {
  const params = await searchParams;
  const q = params.q?.trim() ?? "";
  const data = q
    ? await serverFetch<GlobalSearchResultDto[]>(
        `/api/ops/search?q=${encodeURIComponent(q)}&limit=50`,
      )
    : [];
  if (data === "unauthorized") {
    redirect("/login");
  }
  const results = Array.isArray(data) ? data : [];

  return (
    <div className="space-y-6 px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Search"
        description="Busca apps, app environments, releases, endpoints, machines y data services desde una sola vista."
      />

      <Card>
        <CardContent className="p-4">
          <form method="get" className="flex flex-col gap-3 sm:flex-row">
            <Input
              name="q"
              defaultValue={q}
              placeholder="portal cliente-a prod, hostname, commit, machine..."
              className="min-w-0 flex-1"
              autoFocus
            />
            <Button type="submit">
              <Search className="mr-2 h-4 w-4" />
              Buscar
            </Button>
          </form>
        </CardContent>
      </Card>

      {!q ? (
        <EmptyState
          icon={<Search className="h-6 w-6" />}
          title="Busca cualquier recurso operativo"
          description="No necesitas recordar si vive en Apps, Public Access, Releases, Machines o Data Services."
        />
      ) : data === "error" || data === "notfound" ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo ejecutar la busqueda.
          </CardContent>
        </Card>
      ) : results.length === 0 ? (
        <EmptyState
          icon={<Search className="h-6 w-6" />}
          title="Sin resultados"
          description="Prueba por app, tenant, environment, hostname, commit, machine o status."
        />
      ) : (
        <div className="space-y-2">
          {results.map((result) => (
            <Link
              key={`${result.type}:${result.href}:${result.title}`}
              href={result.href}
              className="block rounded-md border bg-card p-4 transition-colors hover:border-primary/40"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="outline">{result.type}</Badge>
                    {result.status ? <Badge variant={badgeVariant(result.status)}>{result.status}</Badge> : null}
                    {result.badge ? <Badge variant="secondary">{result.badge}</Badge> : null}
                  </div>
                  <p className="mt-2 truncate text-sm font-semibold">{result.title}</p>
                  <p className="mt-1 truncate text-xs text-muted-foreground">{result.subtitle}</p>
                </div>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

function badgeVariant(status: string) {
  const normalized = status.toLowerCase();
  if (normalized === "healthy" || normalized === "ready" || normalized === "completed" || normalized === "owned") {
    return "success";
  }
  if (normalized === "failed" || normalized === "offline" || normalized === "unowned") {
    return "destructive";
  }
  if (normalized === "warning" || normalized === "degraded") {
    return "warning";
  }
  if (normalized === "deploying" || normalized === "busy") {
    return "info";
  }
  return "outline";
}
