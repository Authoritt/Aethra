import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Network, Plus } from "lucide-react";
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
import { CertStatusPill } from "@/components/aethra/cert-status-pill";
import { API_URL } from "@/lib/api";
import type { RouteDto } from "@/lib/types";
import { DeleteRouteButton } from "./delete-route-button";

export const dynamic = "force-dynamic";

async function fetchRoutes(): Promise<RouteDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/proxy/routes`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as RouteDto[];
}

export default async function RoutesPage() {
  const t = await getTranslations("pages.routes_list");
  const tCommon = await getTranslations("common");
  const data = await fetchRoutes();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const routes = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Button asChild>
            <Link href="/routes/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("new_route")}
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error")}
          </CardContent>
        </Card>
      ) : routes.length === 0 ? (
        <EmptyState
          icon={<Network className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/routes/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("create_route")}
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("col_hostname")}</TableHead>
                <TableHead>Path</TableHead>
                <TableHead>{t("col_backend")}</TableHead>
                <TableHead>{t("col_tls")}</TableHead>
                <TableHead>{t("col_expires")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {routes.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <Link
                      href={`/routes/${r.id}`}
                      className="font-medium text-foreground hover:text-primary"
                    >
                      {r.hostname}
                    </Link>
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {r.pathPrefix ?? "/"}
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {r.backendUrl}
                  </TableCell>
                  <TableCell>
                    {r.tlsEnabled ? (
                      <CertStatusPill status={r.certStatus} />
                    ) : (
                      <span className="text-xs text-muted-foreground">
                        edge (Cloudflare)
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatExpires(r.certExpiresAt)}
                  </TableCell>
                  <TableCell className="text-right">
                    <DeleteRouteButton id={r.id} hostname={r.hostname} />
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

function formatExpires(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
  });
}
