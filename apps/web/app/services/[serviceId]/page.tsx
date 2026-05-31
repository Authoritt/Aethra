import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type {
  ManagedServiceDetailDto,
  ServiceBindingDto,
} from "@/lib/types";
import { ServiceStatusPill } from "../ServiceStatusPill";
import { BindingActions } from "../BindingActions";
import { DeleteServiceButton } from "./DeleteServiceButton";

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

async function fetchBindings(serviceId: string): Promise<ServiceBindingDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/${serviceId}/bindings`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return [];
  return (await res.json()) as ServiceBindingDto[];
}

export default async function ServiceDetailPage({
  params,
}: {
  params: Promise<{ serviceId: string }>;
}) {
  const { serviceId } = await params;
  const data = await fetchService(serviceId);
  if (data === "unauthorized") redirect("/login");
  if (data === "notfound") notFound();

  if (data === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el servicio.
        </div>
      </main>
    );
  }

  const service = data;
  const bindings = await fetchBindings(serviceId);
  const activeBindings = bindings.filter((b) => b.revoked_at === null);

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/services" className="hover:text-zinc-300">
            Servicios
          </Link>
          <span> / </span>
          <span className="text-zinc-300">{service.slug}</span>
        </nav>

        <header className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="flex items-center gap-3">
              <h1 className="truncate text-3xl font-semibold">
                {service.name}
              </h1>
              <span className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-300">
                {service.type}
              </span>
              <ServiceStatusPill status={service.status} />
            </div>
            <p className="mt-1 font-mono text-xs text-zinc-500">
              {service.slug}
            </p>
          </div>
          <DeleteServiceButton
            serviceId={service.id}
            slug={service.slug}
            bindingsCount={activeBindings.length}
          />
        </header>

        {service.error_code && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-200">
            <div className="font-medium text-rose-100">
              Error: <span className="font-mono">{service.error_code}</span>
            </div>
            {service.error_message && (
              <p className="mt-1 text-rose-200/90">{service.error_message}</p>
            )}
          </div>
        )}

        <Section title="Configuración">
          <KV label="Imagen" mono value={service.image} />
          <KV label="Versión" mono value={service.version} />
          <KV
            label="Puerto interno"
            mono
            value={String(service.internal_port)}
          />
          <KV label="Network" mono value={service.network_name} />
          <KV label="Container" mono value={service.container_name} />
          <KV label="VM target" mono value={service.target_vm_id} />
          <KV
            label="Expuesto externamente"
            value={service.exposed_externally ? "Sí" : "No (interno)"}
          />
          <KV
            label="Provisionado"
            value={formatDateTime(service.provisioned_at)}
          />
        </Section>

        <section className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm uppercase tracking-wider text-zinc-500">
              Bindings ({activeBindings.length})
            </h2>
            <Link
              href={`/services/${service.id}/bindings/new`}
              className="rounded-full bg-emerald-500 px-4 py-1.5 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              Bindear aplicación
            </Link>
          </div>

          {activeBindings.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-8 text-center text-sm text-zinc-500">
              Aún sin bindings activos. Bindea una application para que pueda
              consumir este servicio.
            </div>
          ) : (
            <ul className="grid grid-cols-1 gap-3">
              {activeBindings.map((b) => (
                <BindingCard key={b.id} binding={b} />
              ))}
            </ul>
          )}
        </section>
      </div>
    </main>
  );
}

function BindingCard({ binding }: { binding: ServiceBindingDto }) {
  const appLabel =
    binding.application_slug ?? binding.application_id.slice(0, 8);
  return (
    <li className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
      <div className="flex items-start justify-between gap-4">
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div className="flex items-center gap-2">
            <h3 className="truncate text-sm font-semibold text-zinc-100">
              {appLabel}
            </h3>
            <PermissionsChip permissions={binding.permissions} />
            {binding.has_migrations_hook && (
              <span className="rounded-full border border-sky-500/40 bg-sky-500/10 px-2 py-0.5 text-[10px] font-medium text-sky-300">
                migrations hook
              </span>
            )}
          </div>
          <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
            <KVInline label="Resource" value={binding.resource_name} mono />
            <KVInline
              label="Env prefix"
              value={binding.env_var_prefix || "—"}
              mono
            />
            <KVInline
              label="Provisionado"
              value={formatDateTime(binding.provisioned_at)}
            />
            <KVInline
              label="Application ID"
              value={binding.application_id}
              mono
            />
          </dl>
        </div>
        <BindingActions bindingId={binding.id} appLabel={appLabel} />
      </div>
    </li>
  );
}

function PermissionsChip({
  permissions,
}: {
  permissions: ServiceBindingDto["permissions"];
}) {
  const styles: Record<ServiceBindingDto["permissions"], string> = {
    Owner: "border-amber-500/40 bg-amber-500/10 text-amber-300",
    ReadWrite: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
    ReadOnly: "border-zinc-700 bg-zinc-800/40 text-zinc-300",
  };
  return (
    <span
      className={`rounded-full border px-2 py-0.5 text-[10px] font-medium ${styles[permissions]}`}
    >
      {permissions}
    </span>
  );
}

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-sm uppercase tracking-wider text-zinc-500">
        {title}
      </h2>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">{children}</div>
    </section>
  );
}

function KV({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-xl border border-zinc-800 bg-zinc-900/40 p-3">
      <div className="text-[10px] uppercase tracking-wider text-zinc-500">
        {label}
      </div>
      <div
        className={`mt-1 truncate text-sm text-zinc-200 ${
          mono ? "font-mono" : ""
        }`}
        title={value}
      >
        {value}
      </div>
    </div>
  );
}

function KVInline({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[10px] uppercase tracking-wider text-zinc-500">
        {label}
      </span>
      <span
        className={`truncate text-zinc-200 ${mono ? "font-mono" : ""}`}
        title={value}
      >
        {value}
      </span>
    </div>
  );
}

function formatDateTime(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
