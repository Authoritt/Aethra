import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { CreatedSecretCard } from "./CreatedSecretCard";

export const dynamic = "force-dynamic";

interface PageProps {
  searchParams: Promise<{ id?: string }>;
}

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

export default async function ApiKeyCreatedPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages.settings_api_keys");
  const tSettings = await getTranslations("pages.settings");

  const ok = await checkAuth();
  if (!ok) redirect("/login");

  const params = await searchParams;
  const id = params.id;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tSettings("title"), href: "/settings" },
          { label: t("list_breadcrumb"), href: "/settings/api-keys" },
          { label: t("created_breadcrumb") },
        ]}
        title={t("created_title")}
        description={t("created_description")}
      />
      <div className="max-w-3xl">
        <CreatedSecretCard id={id ?? null} />
      </div>
    </div>
  );
}
