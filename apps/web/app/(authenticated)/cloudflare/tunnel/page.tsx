import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { CloudflareTunnelDto } from "@/lib/types";
import { TunnelManager } from "./TunnelManager";

export const dynamic = "force-dynamic";

async function fetchTunnel(): Promise<CloudflareTunnelDto | null | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.getAll().map((c) => `${c.name}=${c.value}`).join("; ");
  const res = await fetch(`${API_URL}/api/cloudflare/tunnel/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  const body = (await res.json()) as CloudflareTunnelDto | null;
  return body;
}

export default async function CloudflareTunnelPage() {
  const data = await fetchTunnel();
  if (data === "unauthorized") redirect("/login");
  const tunnel = data === "error" || data === null ? null : data;

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "Cloudflare", href: "/cloudflare" }, { label: "Túnel" }]}
        title="Túnel gestionado (ingress automático)"
        description="Conecta el túnel de Cloudflare para que Aethra agregue/quite el ingress de cada hostname por API — sin reiniciar el túnel (cero corte)."
      />
      <TunnelManager initial={tunnel} loadError={data === "error"} />
    </div>
  );
}
