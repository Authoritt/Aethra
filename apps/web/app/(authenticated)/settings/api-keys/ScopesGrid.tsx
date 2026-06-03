"use client";

import { useMemo } from "react";
import { useTranslations } from "next-intl";
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
  categoryKey: string;
  descriptionKey: string;
  scopes: { value: string; descriptionKey: string }[];
}[] = [
  {
    category: "Projects",
    categoryKey: "cat_projects",
    descriptionKey: "cat_projects_description",
    scopes: [
      { value: "projects:read", descriptionKey: "scope_projects_read" },
      { value: "projects:write", descriptionKey: "scope_projects_write" },
    ],
  },
  {
    category: "Deployments",
    categoryKey: "cat_deployments",
    descriptionKey: "cat_deployments_description",
    scopes: [
      { value: "deployments:read", descriptionKey: "scope_deployments_read" },
      { value: "deployments:write", descriptionKey: "scope_deployments_write" },
      { value: "deployments:trigger", descriptionKey: "scope_deployments_trigger" },
    ],
  },
  {
    category: "Services",
    categoryKey: "cat_services",
    descriptionKey: "cat_services_description",
    scopes: [
      { value: "services:read", descriptionKey: "scope_services_read" },
      { value: "services:write", descriptionKey: "scope_services_write" },
    ],
  },
  {
    category: "Monitoring",
    categoryKey: "cat_monitoring",
    descriptionKey: "cat_monitoring_description",
    scopes: [
      { value: "monitoring:read", descriptionKey: "scope_monitoring_read" },
      { value: "monitoring:write", descriptionKey: "scope_monitoring_write" },
    ],
  },
  {
    category: "Metrics",
    categoryKey: "cat_metrics",
    descriptionKey: "cat_metrics_description",
    scopes: [
      { value: "metrics:read", descriptionKey: "scope_metrics_read" },
    ],
  },
  {
    category: "Cloudflare",
    categoryKey: "cat_cloudflare",
    descriptionKey: "cat_cloudflare_description",
    scopes: [
      { value: "cloudflare:read", descriptionKey: "scope_cloudflare_read" },
      { value: "cloudflare:write", descriptionKey: "scope_cloudflare_write" },
    ],
  },
  {
    category: "VMs",
    categoryKey: "cat_vms",
    descriptionKey: "cat_vms_description",
    scopes: [
      { value: "vms:read", descriptionKey: "scope_vms_read" },
      { value: "vms:write", descriptionKey: "scope_vms_write" },
    ],
  },
  {
    category: "Notes",
    categoryKey: "cat_notes",
    descriptionKey: "cat_notes_description",
    scopes: [
      { value: "notes:read", descriptionKey: "scope_notes_read" },
      { value: "notes:write", descriptionKey: "scope_notes_write" },
    ],
  },
  {
    category: "Context",
    categoryKey: "cat_context",
    descriptionKey: "cat_context_description",
    scopes: [
      { value: "context:read", descriptionKey: "scope_context_read" },
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
  const t = useTranslations("pages.settings_api_keys.scopes");
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
          {t("select_all")}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={selectReadOnly}
          disabled={disabled || readOnlyMatches}
        >
          {t("read_only")}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={clear}
          disabled={disabled || selected.length === 0}
        >
          {t("clear")}
        </Button>
        <span className="ml-auto text-xs text-muted-foreground">
          {isAdmin
            ? t("admin_total_label", { count: "*" })
            : t("scopes_count", {
                count: selected.length,
                total: ALL_NON_ADMIN_SCOPES.length,
              })}
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
            aria-label={t("admin_aria")}
          />
          <div className="flex flex-1 flex-col gap-0.5">
            <div className="flex items-center gap-2 text-sm font-medium text-foreground">
              <ShieldAlert
                className={cn(
                  "h-4 w-4",
                  isAdmin ? "text-warning" : "text-muted-foreground",
                )}
              />
              {t("admin_title")} <span className="font-mono">(*)</span>
            </div>
            <p
              className={cn(
                "text-xs",
                isAdmin ? "text-warning-foreground/80" : "text-muted-foreground",
              )}
            >
              {t("admin_hint")}
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
                  {t(cat.categoryKey)}
                </h4>
                <p className="text-[11px] text-muted-foreground">
                  {t(cat.descriptionKey)}
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
                          {t(s.descriptionKey)}
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
