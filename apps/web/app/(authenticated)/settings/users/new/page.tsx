import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { RoleDto } from "@/lib/types";
import { CreateUserForm } from "../CreateUserForm";

export const dynamic = "force-dynamic";

async function fetchRoles(): Promise<RoleDto[] | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/roles`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return (await res.json()) as RoleDto[];
}

export default async function NewUserPage() {
  const t = await getTranslations("pages.settings_users");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const roles = await fetchRoles();
  if (roles === null) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("settings"), href: "/settings" },
          { label: tBreadcrumbs("users"), href: "/settings/users" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <div className="max-w-3xl">
        <CreateUserForm roles={roles} />
      </div>
    </div>
  );
}
