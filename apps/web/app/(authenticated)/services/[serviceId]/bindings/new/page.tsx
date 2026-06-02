import Link from "next/link";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ManagedServiceDetailDto } from "@/lib/types";
import { NewBindingForm, type ApplicationOption } from "./NewBindingForm";

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

async function fetchApplications(): Promise<ApplicationOption[]> {
  // TODO F9.3+: las "applications" desaparecen en el refactor multi-tenant.
  // El form quedará vacío hasta que migremos bindings a Instances.
  return [];
}

export default async function NewBindingPage({
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
  const apps = await fetchApplications();

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link href="/services" className="hover:text-zinc-300">
            Servicios
          </Link>
          <span> / </span>
          <Link
            href={`/services/${service.id}`}
            className="hover:text-zinc-300"
          >
            {service.slug}
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nuevo binding</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Bindear aplicación</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Conecta una application al servicio{" "}
            <span className="font-mono text-zinc-300">{service.slug}</span>{" "}
            ({service.type}). Aethra crea el recurso aislado y expone las
            credenciales como env vars.
          </p>
        </header>

        <NewBindingForm
          serviceId={service.id}
          serviceType={service.type}
          applications={apps}
        />
      </div>
    </main>
  );
}
