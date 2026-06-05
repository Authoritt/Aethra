import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { serverFetch } from "@/lib/server-fetch";
import type { RoleDto } from "@/lib/types";
import { EditRoleForm } from "./EditRoleForm";

export const dynamic = "force-dynamic";

export default async function EditRolePage({
  params,
}: {
  params: Promise<{ roleId: string }>;
}) {
  const t = await getTranslations("pages.settings_roles");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const { roleId } = await params;

  // No hay GET de un solo rol: traemos la lista y buscamos por id.
  const res = await serverFetch<RoleDto[]>("/api/identity/roles");
  if (res === "unauthorized") redirect("/login");
  if (res === "notfound") notFound();
  if (res === "error") {
    return <div className="px-6 py-8 text-sm text-destructive">No se pudo cargar el rol.</div>;
  }
  const role = res.find((r) => r.id === roleId);
  if (!role) notFound();
  // Los roles de sistema no se pueden editar server-side.
  if (role.isSystem) redirect("/settings/roles");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("settings"), href: "/settings" },
          { label: tBreadcrumbs("users"), href: "/settings/users" },
          { label: tBreadcrumbs("roles"), href: "/settings/roles" },
          { label: role.displayName },
        ]}
        title={`${t("title")}: ${role.displayName}`}
        description={<span className="font-mono text-xs">{role.slug}</span>}
      />
      <div className="max-w-3xl">
        <EditRoleForm role={role} />
      </div>
    </div>
  );
}
