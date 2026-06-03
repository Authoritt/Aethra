import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { CreateIntegrationForm } from "./CreateIntegrationForm";

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

export default async function NewIntegrationPage() {
  const t = await getTranslations("pages.settings_integrations");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("settings"), href: "/settings" },
          { label: tBreadcrumbs("integrations"), href: "/settings/integrations" },
          { label: t("breadcrumb_new") },
        ]}
        title={t("title_new")}
        description={t("description_new")}
      />
      <div className="max-w-2xl">
        <CreateIntegrationForm />
      </div>
    </div>
  );
}
