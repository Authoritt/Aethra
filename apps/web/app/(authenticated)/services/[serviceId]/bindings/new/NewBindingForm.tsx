"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
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

const RUN_ON_OPTIONS: { value: RunOn; label: string }[] = [
  { value: "binding_create", label: "Al crear el binding" },
  { value: "deploy", label: "En cada deploy" },
  { value: "manual", label: "Manual" },
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
  const [hookOpen, setHookOpen] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!applicationId) {
      setError("Selecciona una application.");
      return;
    }
    if (hookEnabled && !hookCommand.trim()) {
      setError("Indica el comando del migrations hook o desactívalo.");
      return;
    }
    setError(null);
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
      router.push(`/services/${serviceId}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const data = e.body as { detail?: string } | undefined;
        setError(data?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  if (applications.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-zinc-800 bg-zinc-900/30 p-8 text-center">
        <p className="text-sm text-zinc-300">
          No tienes applications todavía.
        </p>
        <p className="mt-1 text-xs text-zinc-500">
          Crea una application en un proyecto antes de bindearla a un servicio.
        </p>
        <Link
          href="/projects"
          className="mt-4 inline-block rounded-full border border-zinc-700 px-4 py-1.5 text-xs text-zinc-200 transition hover:bg-zinc-800"
        >
          Ir a proyectos
        </Link>
      </div>
    );
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <Field label="Application" required>
        <select
          value={applicationId}
          onChange={(e) => setApplicationId(e.target.value)}
          className={inputClass}
          required
        >
          {applications.map((a) => (
            <option key={a.id} value={a.id}>
              {a.project_slug} / {a.environment_name} / {a.slug}
            </option>
          ))}
        </select>
      </Field>

      <Field
        label="Resource name"
        hint="Nombre del recurso (database, queue, etc.). Si lo dejas vacío se autogenera del slug de la application."
      >
        <input
          type="text"
          value={resourceName}
          onChange={(e) => setResourceName(e.target.value)}
          placeholder="se autogenera de app.slug"
          className={inputClass}
          autoComplete="off"
          spellCheck={false}
        />
      </Field>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm text-zinc-300">
          Permisos <span className="text-rose-400">*</span>
        </legend>
        <div className="flex flex-col gap-2">
          {PERMS.map((p) => (
            <label
              key={p.value}
              className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm transition ${
                permissions === p.value
                  ? "border-emerald-500/50 bg-emerald-500/5"
                  : "border-zinc-800 bg-zinc-950/40 hover:border-zinc-700"
              }`}
            >
              <input
                type="radio"
                name="permissions"
                value={p.value}
                checked={permissions === p.value}
                onChange={() => setPermissions(p.value)}
                className="mt-0.5 size-4 accent-emerald-500"
              />
              <span className="flex flex-col gap-0.5">
                <span className="font-medium text-zinc-100">{p.label}</span>
                <span className="text-xs text-zinc-500">{p.hint}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <Field
        label="Env var prefix"
        hint='Se añade a las env vars inyectadas (ej. "DB_" → DB_URL, DB_PASSWORD). Vacío para sin prefix.'
      >
        <input
          type="text"
          value={envVarPrefix}
          onChange={(e) => setEnvVarPrefix(e.target.value)}
          placeholder="vacío para sin prefix"
          className={inputClass}
          autoComplete="off"
          spellCheck={false}
        />
      </Field>

      <div className="rounded-lg border border-zinc-800 bg-zinc-950/40">
        <button
          type="button"
          onClick={() => setHookOpen((v) => !v)}
          className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium text-zinc-200 hover:bg-zinc-900/60"
          aria-expanded={hookOpen}
        >
          <span>Migrations hook (opcional)</span>
          <span className="text-xs text-zinc-500">
            {hookOpen ? "ocultar" : "mostrar"}
          </span>
        </button>
        {hookOpen && (
          <div className="flex flex-col gap-4 border-t border-zinc-800 px-4 py-4">
            <label className="flex items-start gap-3 text-sm text-zinc-300">
              <input
                type="checkbox"
                checked={hookEnabled}
                onChange={(e) => setHookEnabled(e.target.checked)}
                className="mt-0.5 size-4 accent-emerald-500"
              />
              <span className="flex flex-col gap-0.5">
                <span className="font-medium text-zinc-100">
                  Activar migrations hook
                </span>
                <span className="text-xs text-zinc-500">
                  Aethra ejecuta el comando dentro del contenedor de la
                  application según el evento que elijas.
                </span>
              </span>
            </label>

            {hookEnabled && (
              <div className="flex flex-col gap-4">
                <Field
                  label="Comando"
                  required
                  hint={
                    serviceType.toLowerCase().includes("postgres")
                      ? 'Ej. "npx prisma migrate deploy" o "alembic upgrade head".'
                      : "Comando a ejecutar dentro del contenedor de la application."
                  }
                >
                  <input
                    type="text"
                    value={hookCommand}
                    onChange={(e) => setHookCommand(e.target.value)}
                    placeholder="npx prisma migrate deploy"
                    className={`${inputClass} font-mono`}
                    autoComplete="off"
                    spellCheck={false}
                  />
                </Field>

                <div className="grid grid-cols-2 gap-3">
                  <Field
                    label="Timeout (s)"
                    hint="Tiempo máximo de ejecución."
                  >
                    <input
                      type="number"
                      min={1}
                      max={3600}
                      value={hookTimeout}
                      onChange={(e) =>
                        setHookTimeout(parseInt(e.target.value, 10) || 0)
                      }
                      className={inputClass}
                    />
                  </Field>
                  <Field label="Disparar en">
                    <select
                      value={hookRunOn}
                      onChange={(e) => setHookRunOn(e.target.value as RunOn)}
                      className={inputClass}
                    >
                      {RUN_ON_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                  </Field>
                </div>

                <label className="flex items-start gap-3 text-sm text-zinc-300">
                  <input
                    type="checkbox"
                    checked={hookFailOnError}
                    onChange={(e) => setHookFailOnError(e.target.checked)}
                    className="mt-0.5 size-4 accent-emerald-500"
                  />
                  <span className="flex flex-col gap-0.5">
                    <span className="font-medium text-zinc-100">
                      Fallar el deploy si el hook falla
                    </span>
                    <span className="text-xs text-zinc-500">
                      Si está activo, un exit code distinto de 0 aborta el
                      deploy y revierte el binding.
                    </span>
                  </span>
                </label>
              </div>
            )}
          </div>
        )}
      </div>

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={() => router.push(`/services/${serviceId}`)}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={loading || !applicationId}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Crear binding"}
        </button>
      </div>
    </form>
  );
}

const inputClass =
  "rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500";

function Field({
  label,
  required,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm text-zinc-300">
      <span>
        {label}
        {required && <span className="text-rose-400"> *</span>}
      </span>
      {children}
      {hint && <span className="text-xs text-zinc-500">{hint}</span>}
    </label>
  );
}
