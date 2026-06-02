"use client";

import { useMemo } from "react";
import { ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";

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
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={selectAll}
          disabled={disabled || allSelected}
        >
          Seleccionar todos
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={selectReadOnly}
          disabled={disabled || readOnlyMatches}
        >
          Solo lectura
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={clear}
          disabled={disabled || selected.length === 0}
        >
          Limpiar
        </Button>
        <span className="ml-auto text-xs text-muted-foreground">
          {isAdmin
            ? "Admin total (*)"
            : `${selected.length}/${ALL_NON_ADMIN_SCOPES.length} scopes`}
        </span>
      </div>

      <Card
        className={cn(
          "transition-colors",
          isAdmin
            ? "border-warning/40 bg-warning/10"
            : "border-border bg-card",
        )}
      >
        <CardContent className="flex items-start gap-3 p-4">
          <Checkbox
            checked={isAdmin}
            onCheckedChange={toggleAdmin}
            disabled={disabled}
            className="mt-0.5"
            aria-label="Admin total"
          />
          <div className="flex flex-1 flex-col gap-0.5">
            <div className="flex items-center gap-2 text-sm font-medium text-foreground">
              <ShieldAlert
                className={cn(
                  "h-4 w-4",
                  isAdmin ? "text-warning" : "text-muted-foreground",
                )}
              />
              Admin total <span className="font-mono">(*)</span>
            </div>
            <p
              className={cn(
                "text-xs",
                isAdmin ? "text-warning-foreground/80" : "text-muted-foreground",
              )}
            >
              Concede acceso a todos los endpoints actuales y futuros. Usá con
              moderación y solo para integraciones de infraestructura.
            </p>
          </div>
        </CardContent>
      </Card>

      <fieldset
        disabled={disabled || isAdmin}
        className={cn(
          "grid grid-cols-1 gap-3 md:grid-cols-2",
          isAdmin && "opacity-40",
        )}
      >
        {SCOPE_CATALOG.map((cat) => (
          <Card key={cat.category} className="bg-card">
            <CardContent className="flex flex-col gap-2 p-4">
              <header>
                <h4 className="text-sm font-semibold text-foreground">
                  {cat.category}
                </h4>
                <p className="text-[11px] text-muted-foreground">
                  {cat.description}
                </p>
              </header>
              <div className="flex flex-col gap-1.5">
                {cat.scopes.map((s) => {
                  const checked = set.has(s.value);
                  return (
                    <label
                      key={s.value}
                      className={cn(
                        "flex cursor-pointer items-start gap-2 rounded-md border p-2 text-xs transition-colors",
                        checked
                          ? "border-primary/40 bg-primary/5"
                          : "border-border bg-background hover:border-border/80 hover:bg-secondary/40",
                      )}
                    >
                      <Checkbox
                        checked={checked}
                        onCheckedChange={() => toggle(s.value)}
                        className="mt-0.5"
                        aria-label={s.value}
                      />
                      <span className="flex min-w-0 flex-col">
                        <span className="font-mono text-[11px] text-foreground">
                          {s.value}
                        </span>
                        <span className="text-[10px] text-muted-foreground">
                          {s.description}
                        </span>
                      </span>
                    </label>
                  );
                })}
              </div>
            </CardContent>
          </Card>
        ))}
      </fieldset>
    </div>
  );
}
