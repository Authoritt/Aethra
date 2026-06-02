import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { CreateBaseDomainForm } from "./CreateBaseDomainForm";

export const dynamic = "force-dynamic";

interface CloudflareZoneOption {
  id: string;
  name: string;
}

async function loadContext(): Promise<
  { authed: true; zones: CloudflareZoneOption[] } | { authed: false }
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");

  const meRes = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!meRes.ok) return { authed: false };

  let zones: CloudflareZoneOption[] = [];
  try {
    const res = await fetch(`${API_URL}/api/cloudflare/zones/`, {
      headers: { cookie: cookieHeader },
      cache: "no-store",
    });
    if (res.ok) {
      const raw = (await res.json()) as unknown;
      if (Array.isArray(raw)) {
        zones = raw
          .map((z) => {
            const obj = z as Record<string, unknown>;
            const id = typeof obj.id === "string" ? obj.id : null;
            const name = typeof obj.name === "string" ? obj.name : null;
            return id && name ? { id, name } : null;
          })
          .filter((v): v is CloudflareZoneOption => v !== null);
      }
    }
  } catch {
    // Ignoramos: el form sigue funcionando sin lista de zonas.
  }

  return { authed: true, zones };
}

export default async function NewBaseDomainPage() {
  const ctx = await loadContext();
  if (!ctx.authed) redirect("/login");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Base domains", href: "/settings/domains" },
          { label: "Nuevo" },
        ]}
        title="Nuevo base domain"
        description="Registrá el FQDN. Opcionalmente enlazalo con una zona ya conocida por el módulo Cloudflare para que la UI vincule ambos recursos."
      />
      <div className="max-w-2xl">
        <CreateBaseDomainForm zones={ctx.zones} />
      </div>
    </div>
  );
}
