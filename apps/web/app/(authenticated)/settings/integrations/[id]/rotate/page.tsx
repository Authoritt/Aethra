import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import type { IntegrationCredentialDto } from "@/lib/types";
import { RotateIntegrationForm } from "./RotateIntegrationForm";

export const dynamic = "force-dynamic";

interface PageProps {
  params: Promise<{ id: string }>;
}

async function fetchCredential(
  id: string,
): Promise<IntegrationCredentialDto | "unauthorized" | "not_found" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/settings/integrations/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  const list = (await res.json()) as IntegrationCredentialDto[];
  const found = list.find((c) => c.id === id);
  return found ?? "not_found";
}

export default async function RotateIntegrationPage({ params }: PageProps) {
  const { id } = await params;
  const data = await fetchCredential(id);

  if (data === "unauthorized") {
    redirect("/login");
  }

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
          <Link
            href="/settings/integrations"
            className="hover:text-zinc-300"
          >
            Integraciones
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Rotar</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Rotar credencial</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Sustituye el valor cifrado por uno nuevo. La metadata (nombre,
            tipo, descripcion) se mantiene. El valor anterior se descarta tras
            el SaveChanges.
          </p>
        </header>

        {data === "error" && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
            No se pudo cargar la credencial. Verifica que la API este
            corriendo.
          </div>
        )}

        {data === "not_found" && (
          <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-200">
            No existe una credencial con id{" "}
            <span className="font-mono">{id}</span>. Es posible que la hayan
            borrado.
            <div className="mt-2">
              <Link
                href="/settings/integrations"
                className="text-emerald-300 hover:underline"
              >
                Volver al listado
              </Link>
            </div>
          </div>
        )}

        {typeof data === "object" && data !== null && (
          <RotateIntegrationForm credential={data} />
        )}
      </div>
    </main>
  );
}
