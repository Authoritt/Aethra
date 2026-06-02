"use client";

import Link from "next/link";
import { Fragment } from "react";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Mapping de slug → label en castellano para la ruta visible.
 * Si un segmento no está acá lo mostramos lowercase tal cual.
 */
const LABELS: Record<string, string> = {
  dashboard: "Dashboard",
  projects: "Proyectos",
  templates: "Plantillas",
  clients: "Clientes",
  instances: "Instancias",
  vms: "VMs",
  services: "Servicios",
  routes: "Routes",
  cloudflare: "Cloudflare",
  monitors: "Monitores",
  builds: "Builds",
  deployments: "Deployments",
  notes: "Notas",
  settings: "Settings",
  integrations: "Integraciones",
  domains: "Dominios",
  environments: "Ambientes",
  "api-keys": "API keys",
  new: "Nuevo",
  edit: "Editar",
  records: "Records",
};

/**
 * Prefijos de IDs estables del modelo de datos. Si un segmento empieza con
 * alguno de estos lo mostramos en mono — son ULIDs/strings opacos.
 */
const ID_PREFIXES = [
  "prj_",
  "tpl_",
  "cli_",
  "ins_",
  "bld_",
  "dep_",
  "vm_",
  "svc_",
  "bnd_",
  "rt_",
  "mon_",
  "cert_",
  "bd_",
  "envd_",
  "int_",
  "key_",
  "note_",
  "zone_",
];

interface Crumb {
  href: string;
  label: string;
  isId: boolean;
}

function buildCrumbs(pathname: string): Crumb[] {
  // Saca el primer "/" y filtra vacíos para "/foo/bar" → ["foo","bar"].
  const segments = pathname.split("/").filter(Boolean);
  if (segments.length === 0) return [];

  // Para que `/projects` no muestre solo "Proyectos" en seco, prependeamos
  // Dashboard como root salvo que ya estemos en /dashboard.
  const crumbs: Crumb[] = [];
  if (segments[0] !== "dashboard") {
    crumbs.push({ href: "/dashboard", label: "Dashboard", isId: false });
  }

  let acc = "";
  for (const seg of segments) {
    acc += `/${seg}`;
    const isId = ID_PREFIXES.some((p) => seg.startsWith(p));
    const label = isId ? seg : (LABELS[seg] ?? decodeURIComponent(seg));
    crumbs.push({ href: acc, label, isId });
  }
  return crumbs;
}

export function Breadcrumbs({ pathname }: { pathname: string }) {
  const crumbs = buildCrumbs(pathname);
  if (crumbs.length === 0) return null;

  return (
    <nav
      aria-label="Breadcrumb"
      className="flex min-w-0 flex-1 items-center gap-1.5 overflow-hidden text-sm"
    >
      <ol className="flex min-w-0 items-center gap-1.5">
        {crumbs.map((crumb, i) => {
          const isLast = i === crumbs.length - 1;
          return (
            <Fragment key={crumb.href}>
              {i > 0 && (
                <ChevronRight
                  className="size-3.5 shrink-0 text-muted-foreground/60"
                  aria-hidden="true"
                />
              )}
              <li className="min-w-0 truncate">
                {isLast ? (
                  <span
                    className={cn(
                      "truncate font-medium text-foreground",
                      crumb.isId && "font-mono text-xs",
                    )}
                    aria-current="page"
                  >
                    {crumb.label}
                  </span>
                ) : (
                  <Link
                    href={crumb.href}
                    className={cn(
                      "truncate text-muted-foreground transition-colors hover:text-foreground",
                      crumb.isId && "font-mono text-xs",
                    )}
                  >
                    {crumb.label}
                  </Link>
                )}
              </li>
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
