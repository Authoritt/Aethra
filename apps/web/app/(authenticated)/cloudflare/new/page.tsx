"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { AlertTriangle, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type {
  CloudflareZoneDto,
  RegisterCloudflareZoneRequest,
} from "@/lib/types";

const ZONE_ID_RE = /^[0-9a-f]{32}$/i;

export default function NewCloudflareZonePage() {
  const router = useRouter();
  const [zoneId, setZoneId] = useState("");
  const [apiToken, setApiToken] = useState("");
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!ZONE_ID_RE.test(zoneId.trim())) {
      return "El zone_id debe ser una cadena hex de 32 caracteres.";
    }
    if (apiToken.trim().length < 8) {
      return "El API token parece demasiado corto.";
    }
    return null;
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    const v = validate();
    if (v) {
      toast.error(v);
      return;
    }
    setLoading(true);
    try {
      const body: RegisterCloudflareZoneRequest = {
        zone_id: zoneId.trim().toLowerCase(),
        api_token: apiToken.trim(),
      };
      const created = await api<CloudflareZoneDto>("/api/cloudflare/zones/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(`Zona "${created.name}" registrada`);
      router.push(`/cloudflare/${created.id}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Cloudflare", href: "/cloudflare" },
          { label: "Nueva zona" },
        ]}
        title="Registrar zona"
        description="Aethra verificará el token contra la API de Cloudflare y guardará la zona con su token cifrado."
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label htmlFor="zone">Zone ID *</Label>
              <Input
                id="zone"
                value={zoneId}
                onChange={(e) => setZoneId(e.target.value)}
                placeholder="023e105f4ecef8ad9ca31a8372d0c353"
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
              <p className="text-xs text-muted-foreground">
                32 caracteres hex. Aparece en el panel de Cloudflare en la
                sidebar derecha (Overview &gt; API).
              </p>
            </div>
            <div className="space-y-2">
              <Label htmlFor="token">API Token *</Label>
              <Input
                id="token"
                type="password"
                value={apiToken}
                onChange={(e) => setApiToken(e.target.value)}
                placeholder="••••••••••••••••"
                autoComplete="off"
                spellCheck={false}
                required
              />
              <p className="text-xs text-muted-foreground">
                Token con scope &apos;Zone.DNS.Edit&apos; sobre esta zona. Aethra
                lo cifra con DataProtection antes de guardar.
              </p>
            </div>

            <Card className="border-warning/30 bg-warning/5">
              <CardContent className="flex items-start gap-3 p-3">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
                <p className="text-xs text-muted-foreground">
                  Creá el token en Cloudflare desde{" "}
                  <em>My Profile &gt; API Tokens</em> con permisos mínimos{" "}
                  <code className="font-mono">Zone:Read</code> y{" "}
                  <code className="font-mono">DNS:Edit</code> limitados a esta
                  zona.
                </p>
              </CardContent>
            </Card>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/cloudflare")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Registrar
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
