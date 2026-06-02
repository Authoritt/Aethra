import { cookies } from "next/headers";
import { redirect } from "next/navigation";
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
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Users", href: "/settings/users" },
          { label: "Roles", href: "/settings/roles" },
          { label: "Nuevo" },
        ]}
        title="Crear rol custom"
        description="Definí un slug, un nombre y los scopes que el rol concede."
      />
      <div className="max-w-3xl">
        <CreateRoleForm />
      </div>
    </div>
  );
}
