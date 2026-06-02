import { cookies } from "next/headers";
import { redirect } from "next/navigation";
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
  const roles = await fetchRoles();
  if (roles === null) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Users", href: "/settings/users" },
          { label: "Nuevo" },
        ]}
        title="Crear usuario"
        description="Definí email, contraseña inicial y los roles que determinan sus permisos."
      />
      <div className="max-w-3xl">
        <CreateUserForm roles={roles} />
      </div>
    </div>
  );
}
