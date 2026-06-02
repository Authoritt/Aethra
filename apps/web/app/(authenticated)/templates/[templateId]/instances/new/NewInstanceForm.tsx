"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
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
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug debe iniciar con letra, lowercase con guiones (max 31 chars).";
  }, [slug]);

  const canSubmit =
    !loading &&
    clientId &&
    environment &&
    targetVmId &&
    slug &&
    !slugError;

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
    setError(null);
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
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      router.push(`/instances/${response.id}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
        <Field label="Client" required>
          <select
            value={clientId}
            onChange={(e) => setClientId(e.target.value)}
            className={inputClass}
            required
            disabled={clients.length === 0}
          >
            {clients.length === 0 ? (
              <option value="">No hay clients en este proyecto</option>
            ) : (
              clients.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.displayName} ({c.slug})
                </option>
              ))
            )}
          </select>
        </Field>
        <Field label="Environment" required>
          <select
            value={environment}
            onChange={(e) => setEnvironment(e.target.value)}
            className={inputClass}
            required
            disabled={environments.length === 0}
          >
            {environments.length === 0 ? (
              <option value="">No hay environments definidos</option>
            ) : (
              environments.map((env) => (
                <option key={env.id} value={env.slug}>
                  {env.displayName} ({env.slug})
                </option>
              ))
            )}
          </select>
        </Field>
        <Field label="VM destino" required>
          <select
            value={targetVmId}
            onChange={(e) => setTargetVmId(e.target.value)}
            className={inputClass}
            required
            disabled={vms.length === 0}
          >
            {vms.length === 0 ? (
              <option value="">No hay VMs registradas</option>
            ) : (
              vms.map((vm) => (
                <option key={vm.id} value={vm.id}>
                  {vm.name} ({vm.slug})
                </option>
              ))
            )}
          </select>
        </Field>
        <Field
          label="Slug"
          required
          hint="Identificador interno. Aethra arma containerName y hostname con esto."
        >
          <input
            type="text"
            value={slug}
            onChange={(e) => setSlug(e.target.value.toLowerCase())}
            placeholder="instance-prod"
            className={`${inputClass} font-mono text-xs`}
            maxLength={31}
            required
          />
          {slugError && (
            <span className="text-[11px] text-rose-400">{slugError}</span>
          )}
        </Field>
      </div>

      <Field
        label="Custom domain"
        hint="Opcional. Si lo dejas vacio se usa el auto-hostname template-client-env.base_domain."
      >
        <input
          type="text"
          value={customDomain}
          onChange={(e) => setCustomDomain(e.target.value)}
          placeholder="app.mi-cliente.com"
          className={`${inputClass} font-mono text-xs`}
        />
      </Field>

      <label className="flex items-center gap-2 text-sm text-zinc-300">
        <input
          type="checkbox"
          checked={autoDeploy}
          onChange={(e) => setAutoDeploy(e.target.checked)}
          className="size-4 rounded border-zinc-700 bg-zinc-950 accent-emerald-500"
        />
        Auto-deploy al detectar un nuevo build verde
      </label>

      <fieldset className="flex flex-col gap-3 rounded-xl border border-zinc-800 bg-zinc-950/40 p-4">
        <div className="flex items-center justify-between">
          <legend className="px-2 text-xs uppercase tracking-wider text-zinc-500">
            Ports
          </legend>
          <button
            type="button"
            onClick={addPort}
            className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
          >
            Anadir puerto
          </button>
        </div>
        {ports.length === 0 ? (
          <p className="text-[11px] text-zinc-500">
            Sin puertos. Anade un puerto si tu container expone uno.
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {ports.map((p, i) => (
              <li key={i} className="grid grid-cols-12 items-center gap-2">
                <input
                  type="number"
                  min={1}
                  max={65535}
                  value={p.containerPort}
                  onChange={(e) =>
                    setPort(i, { containerPort: Number(e.target.value) || 0 })
                  }
                  className={`${inputClass} col-span-3 font-mono text-xs`}
                  placeholder="containerPort"
                />
                <input
                  type="number"
                  min={1}
                  max={65535}
                  value={p.hostPort ?? ""}
                  onChange={(e) =>
                    setPort(i, {
                      hostPort: e.target.value ? Number(e.target.value) : null,
                    })
                  }
                  className={`${inputClass} col-span-3 font-mono text-xs`}
                  placeholder="hostPort (auto)"
                />
                <select
                  value={p.protocol}
                  onChange={(e) =>
                    setPort(i, { protocol: e.target.value as PortProtocol })
                  }
                  className={`${inputClass} col-span-3`}
                >
                  <option value="Tcp">Tcp</option>
                  <option value="Udp">Udp</option>
                </select>
                <button
                  type="button"
                  onClick={() => removePort(i)}
                  className="col-span-3 rounded-full border border-zinc-700 px-2 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
                >
                  Quitar
                </button>
              </li>
            ))}
          </ul>
        )}
      </fieldset>

      <fieldset className="flex flex-col gap-3 rounded-xl border border-zinc-800 bg-zinc-950/40 p-4">
        <div className="flex items-center justify-between">
          <legend className="px-2 text-xs uppercase tracking-wider text-zinc-500">
            Volumes
          </legend>
          <button
            type="button"
            onClick={addVolume}
            className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
          >
            Anadir volume
          </button>
        </div>
        {volumes.length === 0 ? (
          <p className="text-[11px] text-zinc-500">Sin volumes.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {volumes.map((v, i) => (
              <li key={i} className="grid grid-cols-12 items-center gap-2">
                <input
                  type="text"
                  value={v.name}
                  onChange={(e) => setVolume(i, { name: e.target.value })}
                  className={`${inputClass} col-span-3 font-mono text-xs`}
                  placeholder="name"
                />
                <input
                  type="text"
                  value={v.containerPath}
                  onChange={(e) =>
                    setVolume(i, { containerPath: e.target.value })
                  }
                  className={`${inputClass} col-span-5 font-mono text-xs`}
                  placeholder="/data"
                />
                <label className="col-span-2 flex items-center gap-1 text-[11px] text-zinc-300">
                  <input
                    type="checkbox"
                    checked={v.readOnly}
                    onChange={(e) =>
                      setVolume(i, { readOnly: e.target.checked })
                    }
                    className="size-3.5 rounded border-zinc-700 bg-zinc-950 accent-emerald-500"
                  />
                  ro
                </label>
                <button
                  type="button"
                  onClick={() => removeVolume(i)}
                  className="col-span-2 rounded-full border border-zinc-700 px-2 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
                >
                  Quitar
                </button>
              </li>
            ))}
          </ul>
        )}
      </fieldset>

      <div className="rounded-xl border border-zinc-800 bg-zinc-950/40 p-4">
        <button
          type="button"
          onClick={() => setShowHealthcheck((v) => !v)}
          className="flex w-full items-center justify-between text-xs uppercase tracking-wider text-zinc-300"
        >
          <span>Healthcheck</span>
          <span className="text-zinc-500">{showHealthcheck ? "ocultar" : "configurar"}</span>
        </button>

        {showHealthcheck && (
          <div className="mt-4 flex flex-col gap-3">
            <label className="flex items-center gap-2 text-sm text-zinc-300">
              <input
                type="checkbox"
                checked={healthcheck.enabled}
                onChange={(e) =>
                  setHealthcheck((h) => ({ ...h, enabled: e.target.checked }))
                }
                className="size-4 rounded border-zinc-700 bg-zinc-950 accent-emerald-500"
              />
              Habilitar healthcheck
            </label>
            <Field
              label="Comando de test"
              hint="Una linea por argumento. Ej: CMD-SHELL / curl ..."
            >
              <textarea
                value={healthcheck.testRaw}
                onChange={(e) =>
                  setHealthcheck((h) => ({ ...h, testRaw: e.target.value }))
                }
                rows={3}
                className={`${inputClass} font-mono text-xs`}
              />
            </Field>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <Field label="Interval (s)">
                <input
                  type="number"
                  min={1}
                  value={healthcheck.intervalSeconds}
                  onChange={(e) =>
                    setHealthcheck((h) => ({
                      ...h,
                      intervalSeconds: Number(e.target.value) || 0,
                    }))
                  }
                  className={`${inputClass} font-mono text-xs`}
                />
              </Field>
              <Field label="Retries">
                <input
                  type="number"
                  min={0}
                  value={healthcheck.retries}
                  onChange={(e) =>
                    setHealthcheck((h) => ({
                      ...h,
                      retries: Number(e.target.value) || 0,
                    }))
                  }
                  className={`${inputClass} font-mono text-xs`}
                />
              </Field>
              <Field label="Timeout (s)">
                <input
                  type="number"
                  min={0}
                  value={healthcheck.timeoutSeconds}
                  onChange={(e) =>
                    setHealthcheck((h) => ({
                      ...h,
                      timeoutSeconds: e.target.value,
                    }))
                  }
                  className={`${inputClass} font-mono text-xs`}
                />
              </Field>
              <Field label="Start period (s)">
                <input
                  type="number"
                  min={0}
                  value={healthcheck.startPeriodSeconds}
                  onChange={(e) =>
                    setHealthcheck((h) => ({
                      ...h,
                      startPeriodSeconds: e.target.value,
                    }))
                  }
                  className={`${inputClass} font-mono text-xs`}
                />
              </Field>
            </div>
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
          onClick={() => router.push(`/templates/${templateId}`)}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={!canSubmit}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Crear instance"}
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
