import { cookies } from "next/headers";
import { redirect } from "next/navigation";
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
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  const params = await searchParams;
  const id = params.id;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "API keys", href: "/settings/api-keys" },
          { label: "Creada" },
        ]}
        title="API key creada"
        description="Copiá el secret ahora. Es la única vez que podrás verlo: por seguridad solo guardamos su hash en la base."
      />
      <div className="max-w-3xl">
        <CreatedSecretCard id={id ?? null} />
      </div>
    </div>
  );
}
