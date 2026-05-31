import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { CloudflareZoneDto } from "@/lib/types";
import { ZoneStatusPill } from "./ZoneStatusPill";
import { SyncZoneButton } from "./SyncZoneButton";

export const dynamic = "force-dynamic";

async function fetchZones(): Promise<
  CloudflareZoneDto[] | "unauthorized" | "error"
> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/cloudflare/zones/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as CloudflareZoneDto[];
}

export default async function CloudflareZonesPage() {
  const data = await fetchZones();
  if (data === "unauthorized") {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-semibold">Cloudflare</h1>
            <p className="text-sm text-zinc-500">
              Zonas DNS gestionadas via API v4 de Cloudflare. Cada zona usa su
              propio token, cifrado con DataProtection.
            </p>
          </div>
          <Link
            href="/cloudflare/new"
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Registrar zona
          </Link>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el listado. Verifica que la API este corriendo.
          </div>
        )}

        {Array.isArray(data) && data.length === 0 && <EmptyState />}

        {Array.isArray(data) && data.length > 0 && (
          <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            {data.map((zone) => (
              <li
                key={zone.id}
                className="flex flex-col gap-4 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5"
              >
                <header className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Link
                      href={`/cloudflare/${zone.id}`}
                      className="block truncate text-lg font-semibold text-zinc-100 hover:text-emerald-300"
                    >
                      {zone.name}
                    </Link>
                    <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-500">
                      {zone.external_zone_id}
                    </p>
                  </div>
                  <ZoneStatusPill status={zone.status} />
                </header>

                <dl className="grid grid-cols-2 gap-3 text-xs">
                  <div>
                    <dt className="text-[10px] uppercase tracking-wider text-zinc-500">
                      Records
                    </dt>
                    <dd className="mt-0.5 font-mono text-zinc-200">
                      {zone.records_count}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-[10px] uppercase tracking-wider text-zinc-500">
                      Ultimo sync
                    </dt>
                    <dd className="mt-0.5 text-zinc-200">
                      {formatRelative(zone.last_synced_at)}
                    </dd>
                  </div>
                </dl>

                <footer className="flex items-center justify-between border-t border-zinc-800 pt-3">
                  <Link
                    href={`/cloudflare/${zone.id}`}
                    className="text-xs text-zinc-400 hover:text-emerald-300"
                  >
                    Ver records
                  </Link>
                  <SyncZoneButton zoneId={zone.id} />
                </footer>
              </li>
            ))}
          </ul>
        )}
      </div>
    </main>
  );
}

function EmptyState() {
  return (
    <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center">
      <h2 className="text-xl font-semibold text-zinc-100">Aun sin zonas</h2>
      <p className="mt-2 text-sm text-zinc-500">
        Registra una zona de Cloudflare para gestionar sus DNS records desde
        Aethra. Necesitas el zone_id y un API token con scope <em>Zone.DNS</em>.
      </p>
      <Link
        href="/cloudflare/new"
        className="mt-6 inline-block rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
      >
        Registrar zona
      </Link>
    </div>
  );
}

function formatRelative(iso: string | null): string {
  if (!iso) return "nunca";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "-";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "hace unos segundos";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  return `hace ${days} d`;
}
