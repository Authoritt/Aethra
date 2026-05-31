import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { CloudflareZoneDetailDto, DnsRecordDto } from "@/lib/types";
import { ZoneStatusPill } from "../ZoneStatusPill";
import { SyncZoneButton } from "../SyncZoneButton";
import { DeleteRecordButton } from "../DeleteRecordButton";
import { RotateTokenButton } from "./RotateTokenButton";
import { DeleteZoneButton } from "./DeleteZoneButton";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchZone(
  zoneId: string,
): Promise<CloudflareZoneDetailDto | "unauthorized" | "notfound" | "error"> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/cloudflare/zones/${zoneId}`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";
  return (await res.json()) as CloudflareZoneDetailDto;
}

export default async function ZoneDetailPage({
  params,
}: {
  params: Promise<{ zoneId: string }>;
}) {
  const { zoneId } = await params;
  const data = await fetchZone(zoneId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();
  if (data === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando la zona.
        </div>
      </main>
    );
  }

  const zone = data;
  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/cloudflare" className="hover:text-zinc-300">
            Cloudflare
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{zone.name}</span>
        </nav>

        <header className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="flex items-center gap-3">
              <h1 className="truncate text-3xl font-semibold">{zone.name}</h1>
              <ZoneStatusPill status={zone.status} />
            </div>
            <p className="mt-1 font-mono text-xs text-zinc-500">
              zone_id: {zone.external_zone_id}
            </p>
            <p className="font-mono text-xs text-zinc-500">
              account: {zone.account_id}
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <SyncZoneButton zoneId={zone.id} />
            <RotateTokenButton zoneId={zone.id} />
            <DeleteZoneButton
              zoneId={zone.id}
              name={zone.name}
              recordsCount={zone.records.length}
            />
          </div>
        </header>

        <section className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm uppercase tracking-wider text-zinc-500">
              DNS Records ({zone.records.length})
            </h2>
            <Link
              href={`/cloudflare/${zone.id}/records/new`}
              className="rounded-full bg-emerald-500 px-4 py-1.5 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              Crear record
            </Link>
          </div>

          {zone.records.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-8 text-center text-sm text-zinc-500">
              Aun sin records gestionados. Crea uno o sincroniza desde
              Cloudflare para importar los existentes.
            </div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
              <table className="w-full text-left text-sm">
                <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
                  <tr>
                    <th className="px-4 py-3">Tipo</th>
                    <th className="px-4 py-3">Nombre</th>
                    <th className="px-4 py-3">Contenido</th>
                    <th className="px-4 py-3">TTL</th>
                    <th className="px-4 py-3">Proxied</th>
                    <th className="px-4 py-3 text-right">Acciones</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800">
                  {zone.records.map((r) => (
                    <RecordRow key={r.id} record={r} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>
    </main>
  );
}

function RecordRow({ record }: { record: DnsRecordDto }) {
  return (
    <tr className="transition hover:bg-zinc-900/60">
      <td className="px-4 py-3">
        <span className="inline-flex items-center rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-300">
          {record.type}
        </span>
      </td>
      <td className="px-4 py-3 font-mono text-xs text-zinc-100">
        {record.name}
      </td>
      <td
        className="max-w-[24rem] truncate px-4 py-3 font-mono text-xs text-zinc-300"
        title={record.content}
      >
        {record.content}
      </td>
      <td className="px-4 py-3 font-mono text-xs text-zinc-400">
        {record.ttl === 1 ? "auto" : record.ttl}
      </td>
      <td className="px-4 py-3">
        {record.proxied ? (
          <span className="inline-flex items-center gap-1 rounded-full border border-orange-500/40 bg-orange-500/10 px-2 py-0.5 text-[10px] font-medium text-orange-300">
            <span className="size-1.5 rounded-full bg-orange-400" />
            proxied
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] font-medium text-zinc-400">
            <span className="size-1.5 rounded-full bg-zinc-500" />
            dns only
          </span>
        )}
      </td>
      <td className="px-4 py-3 text-right">
        <DeleteRecordButton recordId={record.id} name={record.name} />
      </td>
    </tr>
  );
}
