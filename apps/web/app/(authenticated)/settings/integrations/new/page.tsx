import { cookies } from "next/headers";
import { redirect } from "next/navigation";
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
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Integraciones", href: "/settings/integrations" },
          { label: "Nueva" },
        ]}
        title="Nueva credencial"
        description="El valor en texto plano se cifra con DataProtection y solo se muestra esta vez. Si lo olvidás tendrás que rotar la credencial."
      />
      <div className="max-w-2xl">
        <CreateIntegrationForm />
      </div>
    </div>
  );
}
