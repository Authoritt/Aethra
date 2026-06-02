import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { CreateKeyForm } from "../CreateKeyForm";

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

export default async function NewApiKeyPage() {
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "API keys", href: "/settings/api-keys" },
          { label: "Nueva" },
        ]}
        title="Crear API key"
        description="Definí un nombre, los scopes mínimos necesarios y una expiración opcional. El secret aparecerá una sola vez tras guardar."
      />
      <div className="max-w-3xl">
        <CreateKeyForm />
      </div>
    </div>
  );
}
