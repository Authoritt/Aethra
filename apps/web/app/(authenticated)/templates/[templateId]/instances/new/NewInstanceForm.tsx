"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api } from "@/lib/api";
import type {
  ClientSummary,
  CreateInstanceRequest,
  EnvironmentDefinitionDto,
  InstanceDetail,
  InstancePort,
  InstanceVolume,
  PortProtocol,
  VmDto,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

interface HealthcheckDraft {
  enabled: boolean;
  testRaw: string;
  intervalSeconds: number;
  retries: number;
  timeoutSeconds: string;
  startPeriodSeconds: string;
}

const DEFAULT_HEALTHCHECK: HealthcheckDraft = {
  enabled: false,
  testRaw: "CMD-SHELL\ncurl -fsS http://localhost/health",
  intervalSeconds: 30,
  retries: 3,
  timeoutSeconds: "10",
  startPeriodSeconds: "",
};

export function NewInstanceForm({
  templateId,
  clients,
  environments,
  vms,
}: {
  templateId: string;
  clients: ClientSummary[];
  environments: EnvironmentDefinitionDto[];
  vms: VmDto[];
}) {
  const router = useRouter();
  const [clientId, setClientId] = useState<string>(clients[0]?.id ?? "");
  const [environment, setEnvironment] = useState<string>(
    environments[0]?.slug ?? "",
  );
  const [targetVmId, setTargetVmId] = useState<string>(vms[0]?.id ?? "");
  const [slug, setSlug] = useState("");
  const [autoDeploy, setAutoDeploy] = useState(true);
  const [customDomain, setCustomDomain] = useState("");
  const [ports, setPorts] = useState<InstancePort[]>([]);
  const [volumes, setVolumes] = useState<InstanceVolume[]>([]);
  const [healthcheck, setHealthcheck] =
    useState<HealthcheckDraft>(DEFAULT_HEALTHCHECK);
  const [showHealthcheck, setShowHealthcheck] = useState(false);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug inválido (lowercase + guiones, máx 31).";
  }, [slug]);

  const canSubmit =
    !loading && !!clientId && !!environment && !!targetVmId && !!slug && !slugError;

  function setPort(i: number, patch: Partial<InstancePort>) {
    setPorts((rows) => rows.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
  }
  function addPort() {
    setPorts((rows) => [
      ...rows,
      { containerPort: 80, hostPort: null, protocol: "Tcp" },
    ]);
  }
  function removePort(i: number) {
    setPorts((rows) => rows.filter((_, idx) => idx !== i));
  }

  function setVolume(i: number, patch: Partial<InstanceVolume>) {
    setVolumes((rows) =>
      rows.map((r, idx) => (idx === i ? { ...r, ...patch } : r)),
    );
  }
  function addVolume() {
    setVolumes((rows) => [
      ...rows,
      { name: "", containerPath: "/data", readOnly: false },
    ]);
  }
  function removeVolume(i: number) {
    setVolumes((rows) => rows.filter((_, idx) => idx !== i));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setLoading(true);
    try {
      let hc = null;
      if (showHealthcheck && healthcheck.enabled) {
        const test = healthcheck.testRaw
          .split("\n")
          .map((s) => s.trim())
          .filter(Boolean);
        hc = {
          test,
          intervalSeconds: healthcheck.intervalSeconds,
          retries: healthcheck.retries,
          timeoutSeconds: healthcheck.timeoutSeconds.trim()
            ? Number(healthcheck.timeoutSeconds)
            : null,
          startPeriodSeconds: healthcheck.startPeriodSeconds.trim()
            ? Number(healthcheck.startPeriodSeconds)
            : null,
        };
      }
      const body: CreateInstanceRequest = {
        clientId,
        slug,
        environment,
        targetVmId,
        ports: ports.filter((p) => Number.isFinite(p.containerPort)),
        volumes: volumes.filter((v) => v.name.trim() && v.containerPath.trim()),
        healthcheck: hc,
        autoDeployOnNewBuild: autoDeploy,
        customDomain: customDomain.trim() || null,
      };
      const response = await api<InstanceDetail>(
        `/api/templates/${encodeURIComponent(templateId)}/instances`,
        { method: "POST", body: JSON.stringify(body) },
      );
      toast.success("Instance creada");
      router.push(`/instances/${response.id}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardContent className="space-y-6 p-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div className="space-y-2">
              <Label>Client *</Label>
              <Select
                value={clientId}
                onValueChange={setClientId}
                disabled={clients.length === 0}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Seleccioná un client" />
                </SelectTrigger>
                <SelectContent>
                  {clients.length === 0 ? (
                    <SelectItem value="__none__" disabled>
                      No hay clients en este proyecto
                    </SelectItem>
                  ) : (
                    clients.map((c) => (
                      <SelectItem key={c.id} value={c.id}>
                        {c.displayName} ({c.slug})
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Environment *</Label>
              <Select
                value={environment}
                onValueChange={setEnvironment}
                disabled={environments.length === 0}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Seleccioná un environment" />
                </SelectTrigger>
                <SelectContent>
                  {environments.length === 0 ? (
                    <SelectItem value="__none__" disabled>
                      No hay environments definidos
                    </SelectItem>
                  ) : (
                    environments.map((env) => (
                      <SelectItem key={env.id} value={env.slug}>
                        {env.displayName} ({env.slug})
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>VM destino *</Label>
              <Select
                value={targetVmId}
                onValueChange={setTargetVmId}
                disabled={vms.length === 0}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Seleccioná una VM" />
                </SelectTrigger>
                <SelectContent>
                  {vms.length === 0 ? (
                    <SelectItem value="__none__" disabled>
                      No hay VMs registradas
                    </SelectItem>
                  ) : (
                    vms.map((vm) => (
                      <SelectItem key={vm.id} value={vm.id}>
                        {vm.name} ({vm.slug})
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="slug">Slug *</Label>
              <Input
                id="slug"
                value={slug}
                onChange={(e) => setSlug(e.target.value.toLowerCase())}
                placeholder="instance-prod"
                className="font-mono text-xs"
                maxLength={31}
                required
              />
              {slugError ? (
                <p className="text-xs text-destructive">{slugError}</p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  Aethra arma containerName y hostname con esto.
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="custom">Custom domain</Label>
            <Input
              id="custom"
              value={customDomain}
              onChange={(e) => setCustomDomain(e.target.value)}
              placeholder="app.mi-cliente.com"
              className="font-mono text-xs"
            />
            <p className="text-xs text-muted-foreground">
              Opcional. Si lo dejas vacío se usa el auto-hostname
              template-client-env.base_domain.
            </p>
          </div>

          <div className="flex items-center gap-3 rounded-md border border-border bg-muted/30 p-3">
            <Switch
              id="autodeploy"
              checked={autoDeploy}
              onCheckedChange={setAutoDeploy}
            />
            <Label htmlFor="autodeploy" className="cursor-pointer">
              Auto-deploy al detectar un nuevo build verde
            </Label>
          </div>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-3">
            <div className="flex items-center justify-between">
              <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Ports
              </legend>
              <Button type="button" variant="outline" size="sm" onClick={addPort}>
                <Plus className="mr-2 h-4 w-4" />
                Añadir
              </Button>
            </div>
            {ports.length === 0 ? (
              <p className="text-xs text-muted-foreground">
                Sin puertos. Añadí uno si tu container expone alguno.
              </p>
            ) : (
              <ul className="space-y-2">
                {ports.map((p, i) => (
                  <li key={i} className="grid grid-cols-12 items-center gap-2">
                    <Input
                      type="number"
                      min={1}
                      max={65535}
                      value={p.containerPort}
                      onChange={(e) =>
                        setPort(i, {
                          containerPort: Number(e.target.value) || 0,
                        })
                      }
                      className="col-span-3 font-mono text-xs"
                      placeholder="containerPort"
                    />
                    <Input
                      type="number"
                      min={1}
                      max={65535}
                      value={p.hostPort ?? ""}
                      onChange={(e) =>
                        setPort(i, {
                          hostPort: e.target.value ? Number(e.target.value) : null,
                        })
                      }
                      className="col-span-3 font-mono text-xs"
                      placeholder="hostPort (auto)"
                    />
                    <div className="col-span-4">
                      <Select
                        value={p.protocol}
                        onValueChange={(v) =>
                          setPort(i, { protocol: v as PortProtocol })
                        }
                      >
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="Tcp">Tcp</SelectItem>
                          <SelectItem value="Udp">Udp</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="col-span-2 mx-auto"
                      onClick={() => removePort(i)}
                      aria-label="Quitar"
                    >
                      <Trash2 className="h-4 w-4 text-muted-foreground" />
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </fieldset>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-3">
            <div className="flex items-center justify-between">
              <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Volumes
              </legend>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={addVolume}
              >
                <Plus className="mr-2 h-4 w-4" />
                Añadir
              </Button>
            </div>
            {volumes.length === 0 ? (
              <p className="text-xs text-muted-foreground">Sin volumes.</p>
            ) : (
              <ul className="space-y-2">
                {volumes.map((v, i) => (
                  <li key={i} className="grid grid-cols-12 items-center gap-2">
                    <Input
                      value={v.name}
                      onChange={(e) => setVolume(i, { name: e.target.value })}
                      className="col-span-3 font-mono text-xs"
                      placeholder="name"
                    />
                    <Input
                      value={v.containerPath}
                      onChange={(e) =>
                        setVolume(i, { containerPath: e.target.value })
                      }
                      className="col-span-5 font-mono text-xs"
                      placeholder="/data"
                    />
                    <div className="col-span-2 flex items-center gap-1.5">
                      <Checkbox
                        checked={v.readOnly}
                        onCheckedChange={(checked) =>
                          setVolume(i, { readOnly: Boolean(checked) })
                        }
                      />
                      <Label className="cursor-pointer text-xs">ro</Label>
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="col-span-2 mx-auto"
                      onClick={() => removeVolume(i)}
                      aria-label="Quitar"
                    >
                      <Trash2 className="h-4 w-4 text-muted-foreground" />
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </fieldset>

          <div className="rounded-md border border-border bg-muted/30 p-4">
            <button
              type="button"
              onClick={() => setShowHealthcheck((v) => !v)}
              className="flex w-full items-center justify-between text-xs font-medium uppercase tracking-wider text-foreground"
            >
              <span>Healthcheck</span>
              <span className="text-muted-foreground">
                {showHealthcheck ? "ocultar" : "configurar"}
              </span>
            </button>

            {showHealthcheck ? (
              <div className="mt-4 space-y-3">
                <div className="flex items-center gap-3">
                  <Switch
                    id="hc-enabled"
                    checked={healthcheck.enabled}
                    onCheckedChange={(checked) =>
                      setHealthcheck((h) => ({ ...h, enabled: checked }))
                    }
                  />
                  <Label htmlFor="hc-enabled">Habilitar healthcheck</Label>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="hc-test">Comando de test</Label>
                  <Textarea
                    id="hc-test"
                    value={healthcheck.testRaw}
                    onChange={(e) =>
                      setHealthcheck((h) => ({ ...h, testRaw: e.target.value }))
                    }
                    rows={3}
                    className="font-mono text-xs"
                  />
                  <p className="text-xs text-muted-foreground">
                    Una línea por argumento. Ej: CMD-SHELL / curl ...
                  </p>
                </div>
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  <div className="space-y-2">
                    <Label>Interval (s)</Label>
                    <Input
                      type="number"
                      min={1}
                      value={healthcheck.intervalSeconds}
                      onChange={(e) =>
                        setHealthcheck((h) => ({
                          ...h,
                          intervalSeconds: Number(e.target.value) || 0,
                        }))
                      }
                      className="font-mono text-xs"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Retries</Label>
                    <Input
                      type="number"
                      min={0}
                      value={healthcheck.retries}
                      onChange={(e) =>
                        setHealthcheck((h) => ({
                          ...h,
                          retries: Number(e.target.value) || 0,
                        }))
                      }
                      className="font-mono text-xs"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Timeout (s)</Label>
                    <Input
                      type="number"
                      min={0}
                      value={healthcheck.timeoutSeconds}
                      onChange={(e) =>
                        setHealthcheck((h) => ({
                          ...h,
                          timeoutSeconds: e.target.value,
                        }))
                      }
                      className="font-mono text-xs"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Start period (s)</Label>
                    <Input
                      type="number"
                      min={0}
                      value={healthcheck.startPeriodSeconds}
                      onChange={(e) =>
                        setHealthcheck((h) => ({
                          ...h,
                          startPeriodSeconds: e.target.value,
                        }))
                      }
                      className="font-mono text-xs"
                    />
                  </div>
                </div>
              </div>
            ) : null}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/templates/${templateId}`)}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={!canSubmit}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Crear instance
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
  );
}
