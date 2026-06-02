"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { AlertTriangle, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type { CreateRouteRequest, RouteDto } from "@/lib/types";

const FQDN_RE =
  /^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/i;
const BACKEND_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;

export default function NewRoutePage() {
  const router = useRouter();
  const [hostname, setHostname] = useState("");
  const [backendUrl, setBackendUrl] = useState("");
  const [tlsEnabled, setTlsEnabled] = useState(true);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(hostname.trim()))
      return "El hostname debe ser un FQDN válido (ej. app.example.com).";
    if (!BACKEND_RE.test(backendUrl.trim()))
      return "El backend debe ser una URL http(s) (ej. http://10.0.0.5:8080).";
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
      const body: CreateRouteRequest = {
        hostname: hostname.trim(),
        backend_url: backendUrl.trim(),
        tls_enabled: tlsEnabled,
      };
      await api<RouteDto>("/api/proxy/routes", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Ruta creada");
      router.push("/routes");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string } | undefined)?.detail ??
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
          { label: "Rutas", href: "/routes" },
          { label: "Nueva" },
        ]}
        title="Nueva ruta"
        description="Exponé un backend interno a través del reverse proxy YARP."
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label htmlFor="hostname">Hostname *</Label>
              <Input
                id="hostname"
                value={hostname}
                onChange={(e) => setHostname(e.target.value)}
                placeholder="app.example.com"
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
              <p className="text-xs text-muted-foreground">
                FQDN público que se servirá.
              </p>
            </div>

            <div className="space-y-2">
              <Label htmlFor="backend">Backend URL *</Label>
              <Input
                id="backend"
                value={backendUrl}
                onChange={(e) => setBackendUrl(e.target.value)}
                placeholder="http://10.0.0.5:8080"
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
              <p className="text-xs text-muted-foreground">
                Destino interno al que se enrutará el tráfico.
              </p>
            </div>

            <div className="flex items-start gap-3 rounded-md border border-border bg-muted/30 p-3">
              <Switch
                id="tls"
                checked={tlsEnabled}
                onCheckedChange={setTlsEnabled}
              />
              <div>
                <Label htmlFor="tls" className="cursor-pointer">
                  Habilitar TLS (HTTPS)
                </Label>
                <p className="text-xs text-muted-foreground">
                  Termina TLS en el reverse proxy y redirige HTTP → HTTPS.
                </p>
              </div>
            </div>

            {tlsEnabled ? (
              <Card className="border-warning/30 bg-warning/5">
                <CardContent className="flex items-start gap-3 p-3">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
                  <p className="text-xs text-muted-foreground">
                    Aethra solicitará un certificado Let&apos;s Encrypt
                    automáticamente. El dominio debe apuntar a esta IP y el
                    puerto 80 debe estar abierto para el HTTP-01 challenge.
                  </p>
                </CardContent>
              </Card>
            ) : null}

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/routes")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear ruta
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
