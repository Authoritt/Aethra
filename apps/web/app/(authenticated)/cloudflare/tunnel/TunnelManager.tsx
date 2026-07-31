"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { CheckCircle2, ExternalLink, Loader2, Plug, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ApiError, api } from "@/lib/api";
import type { CloudflareTunnelDto } from "@/lib/types";

export function TunnelManager({
  initial,
  loadError,
}: {
  initial: CloudflareTunnelDto | null;
  loadError: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    accountId: initial?.accountId ?? "",
    tunnelId: initial?.tunnelId ?? "",
    name: initial?.name ?? "",
    apiToken: "",
    aethraService: initial?.aethraService ?? "http://localhost:5080",
    fallbackService: initial?.fallbackService ?? "https://localhost:443",
    targetVmId: initial?.targetVmId ?? "",
  });

  function set<K extends keyof typeof form>(k: K, v: string) {
    setForm((f) => ({ ...f, [k]: v }));
  }

  async function promote() {
    setBusy(true);
    try {
      const r = await api<{ rules: number }>("/api/cloudflare/tunnel/promote-remote", { method: "POST" });
      toast.success(`Config promovida a remota (source=cloudflare) · ${r.rules} reglas`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error promoviendo el túnel",
      );
    } finally {
      setBusy(false);
    }
  }

  async function deployConnector() {
    setBusy(true);
    try {
      const r = await api<{ vmId: string; container: string }>(
        "/api/cloudflare/tunnel/connector/deploy",
        { method: "POST", body: JSON.stringify({}) },
      );
      toast.success(`Connector gestionado desplegado · ${r.container} @ ${r.vmId.slice(0, 8)}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { detail?: string; message?: string } | undefined)?.detail ??
            (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`)
          : "Error desplegando el connector",
      );
    } finally {
      setBusy(false);
    }
  }

  async function register() {
    setBusy(true);
    try {
      await api("/api/cloudflare/tunnel", {
        method: "POST",
        body: JSON.stringify({
          accountId: form.accountId.trim(),
          tunnelId: form.tunnelId.trim(),
          name: form.name.trim(),
          apiToken: form.apiToken.trim(),
          aethraService: form.aethraService.trim(),
          fallbackService: form.fallbackService.trim(),
          fallbackNoTlsVerify: true,
          targetVmId: form.targetVmId.trim() || null,
        }),
      });
      toast.success("Túnel conectado · ingress ahora gestionado por Aethra");
      setForm((f) => ({ ...f, apiToken: "" }));
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error conectando el túnel",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Guía: crear el token */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <ShieldCheck className="h-4 w-4 text-primary" />
            Paso 1 · Crear el token de Cloudflare
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3 text-sm text-muted-foreground">
          <p>
            Aethra necesita un token con permiso de túnel para editar el ingress por API (sin
            reiniciar cloudflared = sin cortes). Créalo una sola vez:
          </p>
          <ol className="ml-4 list-decimal space-y-1.5">
            <li>
              Abre{" "}
              <a
                href="https://dash.cloudflare.com/profile/api-tokens"
                target="_blank"
                rel="noreferrer noopener"
                className="inline-flex items-center gap-1 text-primary hover:underline"
              >
                Cloudflare · API Tokens <ExternalLink className="h-3 w-3" />
              </a>{" "}
              → <strong>Create Token</strong> → <strong>Create Custom Token</strong>.
            </li>
            <li>
              Permissions: <code className="font-mono">Account · Cloudflare Tunnel · Edit</code>.
            </li>
            <li>Account Resources: tu cuenta.</li>
            <li>Crea el token y cópialo (se muestra una sola vez).</li>
          </ol>
          <p className="rounded-md border border-border bg-muted px-3 py-2 text-xs">
            El token se guarda <strong>cifrado</strong> (DataProtection) y nunca se devuelve en claro.
          </p>
        </CardContent>
      </Card>

      {/* Estado actual */}
      {initial ? (
        <Card className="border-primary/30 bg-primary/5">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <CheckCircle2 className="h-4 w-4 text-primary" />
              Túnel conectado · ingress automático activo
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
              <Kv label="Nombre" value={initial.name} />
              <Kv label="Tunnel ID" value={initial.tunnelId} mono />
              <Kv label="Servicio Aethra" value={initial.aethraService} mono />
              <Kv label="Catch-all" value={initial.fallbackService} mono />
            </div>
            <div>
              <div className="mb-1 text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
                Reglas de ingress (en Cloudflare ahora)
              </div>
              {initial.ingress.length === 0 ? (
                <p className="text-xs text-muted-foreground">
                  Sin reglas o token sin acceso (revisa el scope del token).
                </p>
              ) : (
                <Card>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Hostname</TableHead>
                        <TableHead>Servicio</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {initial.ingress.map((r, i) => (
                        <TableRow key={i}>
                          <TableCell className="font-mono text-xs">
                            {r.hostname ?? <span className="text-muted-foreground">catch-all</span>}
                          </TableCell>
                          <TableCell className="font-mono text-xs">{r.service}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Card>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              A partir de ahora, cada deploy o cambio de URL agrega/quita su regla aquí
              automáticamente — sin reiniciar el túnel.
            </p>
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted/40 p-3">
              <Button type="button" variant="outline" size="sm" onClick={promote} disabled={busy}>
                {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}
                Promover config a remota (source=cloudflare)
              </Button>
              <span className="text-[11px] text-muted-foreground">
                Re-publica la config de ingress por API (idempotente). Necesario una vez para que el
                connector la aplique al correr con token.
              </span>
            </div>
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted/40 p-3">
              <Button type="button" variant="outline" size="sm" onClick={deployConnector} disabled={busy}>
                {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Plug className="mr-2 h-4 w-4" />}
                Desplegar connector gestionado (contenedor)
              </Button>
              <span className="text-[11px] text-muted-foreground">
                Corre cloudflared con el connector token como contenedor en la VM (network host). Es
                réplica HA del túnel — el flip a remoto queda 100% desde aquí, sin SSH ni tocar el systemd.
              </span>
            </div>
          </CardContent>
        </Card>
      ) : loadError ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo leer el estado del túnel.
          </CardContent>
        </Card>
      ) : null}

      {/* Form: conectar / actualizar */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Plug className="h-4 w-4 text-primary" />
            Paso 2 · {initial ? "Actualizar token / servicios" : "Conectar el túnel"}
          </CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field label="Account ID (hex 32)">
            <Input value={form.accountId} onChange={(e) => set("accountId", e.target.value)} placeholder="b07a5289…" className="font-mono text-xs" />
          </Field>
          <Field label="Tunnel ID (UUID)">
            <Input value={form.tunnelId} onChange={(e) => set("tunnelId", e.target.value)} placeholder="ca75b591-…" className="font-mono text-xs" />
          </Field>
          <Field label="Nombre del túnel">
            <Input value={form.name} onChange={(e) => set("name", e.target.value)} placeholder="my-apps" />
          </Field>
          <Field label="API Token (Tunnel:Edit)">
            <Input value={form.apiToken} onChange={(e) => set("apiToken", e.target.value)} type="password" placeholder={initial ? "(dejar vacío = sin cambio)" : "pega el token"} className="font-mono text-xs" />
          </Field>
          <Field label="Servicio Aethra (proxy central)">
            <Input value={form.aethraService} onChange={(e) => set("aethraService", e.target.value)} className="font-mono text-xs" />
          </Field>
          <Field label="Catch-all (apps legacy, ej. Traefik)">
            <Input value={form.fallbackService} onChange={(e) => set("fallbackService", e.target.value)} className="font-mono text-xs" />
          </Field>
          <Field label="VM del connector (vm_… donde corren los servicios)">
            <Input value={form.targetVmId} onChange={(e) => set("targetVmId", e.target.value)} placeholder="vm_… (la VM con localhost:5080)" className="font-mono text-xs" />
          </Field>
          <div className="md:col-span-2 flex justify-end">
            <Button type="button" onClick={register} disabled={busy || !form.apiToken.trim()}>
              {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Plug className="mr-2 h-4 w-4" />}
              {initial ? "Actualizar túnel" : "Conectar túnel"}
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">{label}</span>
      {children}
    </label>
  );
}

function Kv({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">{label}</div>
      <div className={`mt-0.5 ${mono ? "font-mono text-[11px]" : ""}`}>{value}</div>
    </div>
  );
}
