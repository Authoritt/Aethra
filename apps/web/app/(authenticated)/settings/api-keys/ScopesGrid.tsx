"use client";

import { useMemo } from "react";

/**
 * Catalogo de scopes disponibles (mantener alineado con backend Modules.Identity).
 * El scope `*` es admin total y excluye al resto cuando esta activo.
 */
export const SCOPE_CATALOG: {
  category: string;
  description: string;
  scopes: { value: string; description: string }[];
}[] = [
  {
    category: "Projects",
    description: "Proyectos, environments y applications.",
    scopes: [
      { value: "projects:read", description: "Listar y leer projects/envs/apps." },
      { value: "projects:write", description: "Crear y modificar projects/envs/apps." },
    ],
  },
  {
    category: "Deployments",
    description: "Pipelines git -> docker.",
    scopes: [
      { value: "deployments:read", description: "Leer jobs y logs de deploy." },
      { value: "deployments:write", description: "Cancelar / configurar deploys." },
      { value: "deployments:trigger", description: "Lanzar deploys manuales o via webhook." },
    ],
  },
  {
    category: "Services",
    description: "Managed services y bindings.",
    scopes: [
      { value: "services:read", description: "Listar plantillas y servicios." },
      { value: "services:write", description: "Crear servicios y bindings." },
    ],
  },
  {
    category: "Monitoring",
    description: "Monitores HTTP de uptime.",
    scopes: [
      { value: "monitoring:read", description: "Leer monitores y checks." },
      { value: "monitoring:write", description: "Crear y modificar monitores." },
    ],
  },
  {
    category: "Metrics",
    description: "Telemetria de VMs.",
    scopes: [
      { value: "metrics:read", description: "Leer series temporales de CPU/RAM/red." },
    ],
  },
  {
    category: "Cloudflare",
    description: "Zonas DNS y records.",
    scopes: [
      { value: "cloudflare:read", description: "Listar zonas y records." },
      { value: "cloudflare:write", description: "Sincronizar y modificar records." },
    ],
  },
  {
    category: "VMs",
    description: "Hosts gestionados con satelite.",
    scopes: [
      { value: "vms:read", description: "Listar VMs y su estado." },
      { value: "vms:write", description: "Registrar y desregistrar VMs." },
    ],
  },
  {
    category: "Notes",
    description: "Notas y pinned facts del workspace.",
    scopes: [
      { value: "notes:read", description: "Leer notas y facts." },
      { value: "notes:write", description: "Crear y editar notas." },
    ],
  },
  {
    category: "Context",
    description: "Snapshot global para agentes IA.",
    scopes: [
      { value: "context:read", description: "Leer el contexto consolidado /context." },
    ],
  },
];

export const ADMIN_SCOPE = "*";

const ALL_NON_ADMIN_SCOPES = SCOPE_CATALOG.flatMap((c) =>
  c.scopes.map((s) => s.value),
);

const READ_ONLY_SCOPES = ALL_NON_ADMIN_SCOPES.filter((s) => s.endsWith(":read"));

export interface ScopesGridProps {
  selected: string[];
  onChange: (next: string[]) => void;
  disabled?: boolean;
}

export function ScopesGrid({ selected, onChange, disabled }: ScopesGridProps) {
  const set = useMemo(() => new Set(selected), [selected]);
  const isAdmin = set.has(ADMIN_SCOPE);
  const allSelected =
    !isAdmin &&
    ALL_NON_ADMIN_SCOPES.every((s) => set.has(s)) &&
    ALL_NON_ADMIN_SCOPES.length > 0;
  const readOnlyMatches =
    !isAdmin &&
    READ_ONLY_SCOPES.every((s) => set.has(s)) &&
    selected.length === READ_ONLY_SCOPES.length;

  function toggle(scope: string) {
    if (disabled) return;
    if (scope === ADMIN_SCOPE) {
      onChange(isAdmin ? [] : [ADMIN_SCOPE]);
      return;
    }
    if (isAdmin) {
      // Si admin estaba activo, lo reemplazamos por el scope individual.
      onChange([scope]);
      return;
    }
    const next = new Set(set);
    if (next.has(scope)) next.delete(scope);
    else next.add(scope);
    onChange(Array.from(next).sort());
  }

  function selectAll() {
    if (disabled) return;
    onChange([...ALL_NON_ADMIN_SCOPES].sort());
  }

  function selectReadOnly() {
    if (disabled) return;
    onChange([...READ_ONLY_SCOPES].sort());
  }

  function clear() {
    if (disabled) return;
    onChange([]);
  }

  function toggleAdmin() {
    if (disabled) return;
    onChange(isAdmin ? [] : [ADMIN_SCOPE]);
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={selectAll}
          disabled={disabled || allSelected}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-40"
        >
          Seleccionar todos
        </button>
        <button
          type="button"
          onClick={selectReadOnly}
          disabled={disabled || readOnlyMatches}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-40"
        >
          Solo lectura
        </button>
        <button
          type="button"
          onClick={clear}
          disabled={disabled || selected.length === 0}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-400 transition hover:bg-zinc-800 disabled:opacity-40"
        >
          Limpiar
        </button>
        <span className="ml-auto text-xs text-zinc-500">
          {isAdmin
            ? "Admin total (*)"
            : `${selected.length}/${ALL_NON_ADMIN_SCOPES.length} scopes`}
        </span>
      </div>

      <label
        className={`flex items-start gap-3 rounded-xl border p-3 text-sm transition ${
          isAdmin
            ? "border-amber-500/40 bg-amber-500/10 text-amber-100"
            : "border-zinc-800 bg-zinc-950/40 text-zinc-300"
        }`}
      >
        <input
          type="checkbox"
          checked={isAdmin}
          onChange={toggleAdmin}
          disabled={disabled}
          className="mt-0.5 size-4 accent-amber-500"
        />
        <span className="flex flex-col gap-0.5">
          <span className="font-medium">
            Admin total <span className="font-mono">(*)</span>
          </span>
          <span className="text-xs text-amber-200/80">
            Concede acceso a todos los endpoints actuales y futuros. Usa con
            moderacion y solo para integraciones de infraestructura.
          </span>
        </span>
      </label>

      <fieldset
        disabled={disabled || isAdmin}
        className={`grid grid-cols-1 gap-3 md:grid-cols-2 ${
          isAdmin ? "opacity-40" : ""
        }`}
      >
        {SCOPE_CATALOG.map((cat) => (
          <div
            key={cat.category}
            className="flex flex-col gap-2 rounded-xl border border-zinc-800 bg-zinc-900/40 p-4"
          >
            <header>
              <h4 className="text-sm font-semibold text-zinc-100">
                {cat.category}
              </h4>
              <p className="text-[11px] text-zinc-500">{cat.description}</p>
            </header>
            <div className="flex flex-col gap-1.5">
              {cat.scopes.map((s) => {
                const checked = set.has(s.value);
                return (
                  <label
                    key={s.value}
                    className={`flex items-start gap-2 rounded-lg border p-2 text-xs transition ${
                      checked
                        ? "border-emerald-500/40 bg-emerald-500/5"
                        : "border-zinc-800 bg-zinc-950/40 hover:border-zinc-700"
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggle(s.value)}
                      className="mt-0.5 size-3.5 accent-emerald-500"
                    />
                    <span className="flex min-w-0 flex-col">
                      <span className="font-mono text-[11px] text-zinc-100">
                        {s.value}
                      </span>
                      <span className="text-[10px] text-zinc-500">
                        {s.description}
                      </span>
                    </span>
                  </label>
                );
              })}
            </div>
          </div>
        ))}
      </fieldset>
    </div>
  );
}
