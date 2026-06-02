import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Card, CardContent } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
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
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Integraciones", href: "/settings/integrations" },
          { label: "Rotar" },
        ]}
        title="Rotar credencial"
        description="Sustituye el valor cifrado por uno nuevo. La metadata (nombre, tipo, descripción) se mantiene. El valor anterior se descarta tras el SaveChanges."
      />

      <div className="max-w-2xl">
        {data === "error" ? (
          <Card className="border-destructive/30 bg-destructive/5">
            <CardContent className="p-4 text-sm text-destructive">
              No se pudo cargar la credencial.
            </CardContent>
          </Card>
        ) : data === "not_found" ? (
          <Card className="border-warning/30 bg-warning/5">
            <CardContent className="p-4 text-sm">
              No existe una credencial con id{" "}
              <span className="font-mono">{id}</span>. Es posible que la hayan
              borrado.
              <div className="mt-2">
                <Link
                  href="/settings/integrations"
                  className="text-primary underline-offset-4 hover:underline"
                >
                  Volver al listado
                </Link>
              </div>
            </CardContent>
          </Card>
        ) : (
          <RotateIntegrationForm credential={data} />
        )}
      </div>
    </div>
  );
}
