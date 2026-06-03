import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { CreateRoleForm } from "../CreateRoleForm";

export const dynamic = "force-dynamic";

async function checkAuth(): Promise<boolean> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  return res.ok;
}

export default async function NewRolePage() {
  const t = await getTranslations("pages.settings_roles");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("settings"), href: "/settings" },
          { label: tBreadcrumbs("users"), href: "/settings/users" },
          { label: tBreadcrumbs("roles"), href: "/settings/roles" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <div className="max-w-3xl">
        <CreateRoleForm />
      </div>
    </div>
  );
}
