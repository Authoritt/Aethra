import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Lock, Plus, Shield } from "lucide-react";
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
import { API_URL } from "@/lib/api";
import type { RoleDto } from "@/lib/types";
import { DeleteRoleButton } from "./DeleteRoleButton";

export const dynamic = "force-dynamic";

async function fetchRoles(): Promise<RoleDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/roles`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401 || res.status === 403) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as RoleDto[];
}

export default async function RolesPage() {
  const t = await getTranslations("pages.settings_roles");
  const tSettings = await getTranslations("pages.settings");
  const tUsers = await getTranslations("pages.settings_users");
  const tCommon = await getTranslations("common");

  const data = await fetchRoles();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const roles = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tSettings("title"), href: "/settings" },
          { label: tUsers("list_breadcrumb"), href: "/settings/users" },
          { label: t("list_breadcrumb") },
        ]}
        title={t("list_title")}
        description={t("list_description")}
        actions={
          <Button asChild>
            <Link href="/settings/roles/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("list_action_new")}
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error_short")}
          </CardContent>
        </Card>
      ) : roles.length === 0 ? (
        <EmptyState
          icon={<Shield className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("col_role")}</TableHead>
                <TableHead>{t("col_slug")}</TableHead>
                <TableHead>{t("col_scopes")}</TableHead>
                <TableHead>{t("col_type")}</TableHead>
                <TableHead className="text-right">{t("col_actions")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {roles.map((r) => (
                <TableRow key={r.id}>
                  <TableCell className="align-top font-medium">
                    {r.displayName}
                  </TableCell>
                  <TableCell className="align-top">
                    <Badge variant="outline" className="font-mono text-[10px]">
                      {r.slug}
                    </Badge>
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex flex-wrap gap-1">
                      {r.scopes.includes("*") ? (
                        <Badge variant="warning" className="text-[10px]">
                          {t("scope_admin")}
                        </Badge>
                      ) : (
                        <>
                          {r.scopes.slice(0, 5).map((s) => (
                            <Badge
                              key={s}
                              variant="outline"
                              className="font-mono text-[10px]"
                            >
                              {s}
                            </Badge>
                          ))}
                          {r.scopes.length > 5 ? (
                            <Badge
                              variant="outline"
                              className="font-mono text-[10px]"
                            >
                              +{r.scopes.length - 5}
                            </Badge>
                          ) : null}
                        </>
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    {r.isSystem ? (
                      <Badge variant="outline" className="text-[10px]">
                        <Lock className="mr-1 h-3 w-3" />
                        {t("type_builtin")}
                      </Badge>
                    ) : (
                      <Badge variant="success" className="text-[10px]">
                        {t("type_custom")}
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    {r.isSystem ? (
                      <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
                        {t("protected_label")}
                      </span>
                    ) : (
                      <DeleteRoleButton id={r.id} slug={r.slug} />
                    )}
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
