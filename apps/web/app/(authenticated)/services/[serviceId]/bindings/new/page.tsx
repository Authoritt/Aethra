import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { ManagedServiceDetailDto } from "@/lib/types";
import { NewBindingForm, type ApplicationOption } from "./NewBindingForm";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchService(
  serviceId: string,
): Promise<ManagedServiceDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/${serviceId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as ManagedServiceDetailDto;
}

async function fetchApplications(): Promise<ApplicationOption[]> {
  // TODO F9.3+: las "applications" desaparecen en el refactor multi-tenant.
  // El form quedará vacío hasta que migremos bindings a Instances.
  return [];
}

export default async function NewBindingPage({
  params,
}: {
  params: Promise<{ serviceId: string }>;
}) {
  const t = await getTranslations("pages.services_bindings_new");
  const { serviceId } = await params;
  const data = await fetchService(serviceId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <div className="px-6 py-8 md:px-10 md:py-10">
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {t("load_error")}
          </CardContent>
        </Card>
      </div>
    );
  }

  const service = data;
  const apps = await fetchApplications();

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: t("breadcrumb_root"), href: "/services" },
          { label: service.slug, href: `/services/${service.id}` },
          { label: t("breadcrumb_current") },
        ]}
        title={t("title")}
        description={
          <>
            {t("description_prefix")}
            <span className="font-mono text-foreground">{service.slug}</span> (
            {service.type}){t("description_suffix")}
          </>
        }
      />
      <div className="max-w-2xl">
        <NewBindingForm
          serviceId={service.id}
          serviceType={service.type}
          applications={apps}
        />
      </div>
    </div>
  );
}
