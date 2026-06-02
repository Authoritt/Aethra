import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { ServiceTemplateDto, VmDto } from "@/lib/types";
import { TemplatePicker } from "./TemplatePicker";

export const dynamic = "force-dynamic";

async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

async function fetchTemplates(): Promise<
  ServiceTemplateDto[] | "unauthorized" | "error"
> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/services/templates`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as ServiceTemplateDto[];
}

async function fetchVms(): Promise<VmDto[]> {
  const cookieHeader = await buildCookieHeader();
  const res = await fetch(`${API_URL}/api/vms/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return [];
  return (await res.json()) as VmDto[];
}

export default async function NewServicePage() {
  const templates = await fetchTemplates();
  if (templates === "unauthorized") redirect("/login");

  const vms = await fetchVms();

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-4xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/services" className="hover:text-zinc-300">
            Servicios
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nuevo</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Crear servicio</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Elige una plantilla. Aethra crea el contenedor con red interna y
            credenciales aisladas listas para bindear desde una application.
          </p>
        </header>

        {templates === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar el catálogo de plantillas.
          </div>
        )}

        {Array.isArray(templates) && templates.length === 0 && (
          <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-12 text-center text-sm text-zinc-400">
            No hay plantillas disponibles.
          </div>
        )}

        {Array.isArray(templates) && templates.length > 0 && (
          <TemplatePicker templates={templates} vms={vms} />
        )}
      </div>
    </main>
  );
}
