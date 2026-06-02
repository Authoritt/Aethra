import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
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

  // El listado de zonas se sirve "best-effort": si falla la llamada al modulo
  // Cloudflare (deshabilitado, sin token, etc.) seguimos permitiendo crear el
  // base domain sin enlazar zona — siempre puede enlazarse despues.
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
            const id =
              typeof obj.id === "string" ? obj.id : null;
            // Aceptamos ambos casings por si la API cambia su naming policy.
            const name =
              typeof obj.name === "string" ? obj.name : null;
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
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-2xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <Link href="/settings" className="hover:text-zinc-300">
            Settings
          </Link>
          <span> / </span>
          <Link href="/settings/domains" className="hover:text-zinc-300">
            Base domains
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nuevo</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Nuevo base domain</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Registra el FQDN. Opcionalmente enlazalo con una zona ya conocida
            por el modulo Cloudflare para que la UI vincule ambos recursos.
          </p>
        </header>

        <CreateBaseDomainForm zones={ctx.zones} />
      </div>
    </main>
  );
}
