"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
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
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type {
  BindingPermissions,
  CreateBindingRequest,
  ServiceBindingDto,
} from "@/lib/types";

export interface ApplicationOption {
  id: string;
  slug: string;
  name: string;
  project_slug: string;
  environment_name: string;
}

type RunOn = "binding_create" | "deploy" | "manual";

const PERMS: { value: BindingPermissions; label: string; hint: string }[] = [
  {
    value: "Owner",
    label: "Owner",
    hint: "Acceso total. Recomendado para migraciones y administración.",
  },
  {
    value: "ReadWrite",
    label: "ReadWrite",
    hint: "Lectura y escritura sobre el recurso, sin DDL ni admin.",
  },
  {
    value: "ReadOnly",
    label: "ReadOnly",
    hint: "Solo lectura. Útil para dashboards, reportes o réplicas.",
  },
];

export function NewBindingForm({
  serviceId,
  serviceType,
  applications,
}: {
  serviceId: string;
  serviceType: string;
  applications: ApplicationOption[];
}) {
  const router = useRouter();
  const [applicationId, setApplicationId] = useState<string>(
    applications[0]?.id ?? "",
  );
  const [resourceName, setResourceName] = useState("");
  const [permissions, setPermissions] = useState<BindingPermissions>("Owner");
  const [envVarPrefix, setEnvVarPrefix] = useState("");

  const [hookEnabled, setHookEnabled] = useState(false);
  const [hookCommand, setHookCommand] = useState("");
  const [hookTimeout, setHookTimeout] = useState(120);
  const [hookFailOnError, setHookFailOnError] = useState(true);
  const [hookRunOn, setHookRunOn] = useState<RunOn>("binding_create");
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!applicationId) {
      toast.error("Seleccioná una application.");
      return;
    }
    if (hookEnabled && !hookCommand.trim()) {
      toast.error("Indicá el comando del migrations hook o desactivalo.");
      return;
    }
    setLoading(true);
    try {
      const body: CreateBindingRequest = {
        application_id: applicationId,
        permissions,
      };
      if (resourceName.trim()) body.resource_name = resourceName.trim();
      if (envVarPrefix.trim()) body.env_var_prefix = envVarPrefix.trim();
      if (hookEnabled) {
        body.migrations_hook = {
          command: hookCommand.trim(),
          timeout_seconds: Number.isFinite(hookTimeout) ? hookTimeout : 120,
          fail_on_error: hookFailOnError,
          run_on: hookRunOn,
        };
      }
      await api<ServiceBindingDto>(`/api/services/${serviceId}/bindings`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Binding creado");
      router.push(`/services/${serviceId}`);
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

  if (applications.length === 0) {
    return (
      <EmptyState
        title="Aún sin aplicaciones"
        description="Creá una application en un proyecto antes de bindearla a un servicio."
        action={
          <Button asChild variant="outline">
            <Link href="/projects">Ir a proyectos</Link>
          </Button>
        }
      />
    );
  }

  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardContent className="space-y-5 p-6">
          <div className="space-y-2">
            <Label>Application *</Label>
            <Select value={applicationId} onValueChange={setApplicationId}>
              <SelectTrigger>
                <SelectValue placeholder="Seleccioná una application" />
              </SelectTrigger>
              <SelectContent>
                {applications.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.project_slug} / {a.environment_name} / {a.slug}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="resource">Resource name</Label>
            <Input
              id="resource"
              value={resourceName}
              onChange={(e) => setResourceName(e.target.value)}
              placeholder="se autogenera de app.slug"
              autoComplete="off"
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              Nombre del recurso (database, queue, etc.). Si lo dejas vacío se
              autogenera del slug de la application.
            </p>
          </div>

          <div className="space-y-2">
            <Label>Permisos *</Label>
            <div className="flex flex-col gap-2">
              {PERMS.map((p) => (
                <label
                  key={p.value}
                  className={cn(
                    "flex cursor-pointer items-start gap-3 rounded-md border p-3 text-sm transition",
                    permissions === p.value
                      ? "border-primary/40 bg-primary/5"
                      : "border-border bg-muted/30 hover:border-foreground/20",
                  )}
                >
                  <input
                    type="radio"
                    name="permissions"
                    value={p.value}
                    checked={permissions === p.value}
                    onChange={() => setPermissions(p.value)}
                    className="mt-0.5 size-4 accent-primary"
                  />
                  <span className="flex flex-col gap-0.5">
                    <span className="font-medium text-foreground">
                      {p.label}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {p.hint}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="prefix">Env var prefix</Label>
            <Input
              id="prefix"
              value={envVarPrefix}
              onChange={(e) => setEnvVarPrefix(e.target.value)}
              placeholder="vacío para sin prefix"
              className="font-mono text-xs"
              autoComplete="off"
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              Se añade a las env vars inyectadas (ej. &quot;DB_&quot; → DB_URL,
              DB_PASSWORD).
            </p>
          </div>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-3">
            <div className="flex items-start gap-3">
              <Switch
                id="hook"
                checked={hookEnabled}
                onCheckedChange={setHookEnabled}
              />
              <div>
                <Label htmlFor="hook" className="cursor-pointer">
                  Activar migrations hook
                </Label>
                <p className="text-xs text-muted-foreground">
                  Aethra ejecuta el comando dentro del contenedor de la
                  application según el evento que elijas.
                </p>
              </div>
            </div>

            {hookEnabled ? (
              <div className="space-y-4 pt-2">
                <div className="space-y-2">
                  <Label htmlFor="hookcmd">Comando *</Label>
                  <Input
                    id="hookcmd"
                    value={hookCommand}
                    onChange={(e) => setHookCommand(e.target.value)}
                    placeholder="npx prisma migrate deploy"
                    className="font-mono text-xs"
                    autoComplete="off"
                    spellCheck={false}
                  />
                  <p className="text-xs text-muted-foreground">
                    {serviceType.toLowerCase().includes("postgres")
                      ? 'Ej. "npx prisma migrate deploy" o "alembic upgrade head".'
                      : "Comando a ejecutar dentro del contenedor de la application."}
                  </p>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-2">
                    <Label htmlFor="timeout">Timeout (s)</Label>
                    <Input
                      id="timeout"
                      type="number"
                      min={1}
                      max={3600}
                      value={hookTimeout}
                      onChange={(e) =>
                        setHookTimeout(parseInt(e.target.value, 10) || 0)
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Disparar en</Label>
                    <Select
                      value={hookRunOn}
                      onValueChange={(v) => setHookRunOn(v as RunOn)}
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="binding_create">
                          Al crear el binding
                        </SelectItem>
                        <SelectItem value="deploy">En cada deploy</SelectItem>
                        <SelectItem value="manual">Manual</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="flex items-start gap-3">
                  <Switch
                    id="failon"
                    checked={hookFailOnError}
                    onCheckedChange={setHookFailOnError}
                  />
                  <div>
                    <Label htmlFor="failon" className="cursor-pointer">
                      Fallar el deploy si el hook falla
                    </Label>
                    <p className="text-xs text-muted-foreground">
                      Si está activo, un exit code distinto de 0 aborta el
                      deploy y revierte el binding.
                    </p>
                  </div>
                </div>
              </div>
            ) : null}
          </fieldset>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/services/${serviceId}`)}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={loading || !applicationId}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Crear binding
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
  );
}
