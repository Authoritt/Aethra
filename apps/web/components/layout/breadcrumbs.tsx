"use client";

import Link from "next/link";
import { Fragment } from "react";
import { ChevronRight } from "lucide-react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

/**
 * Mapping de slug → key del namespace `breadcrumbs`. Si un segmento no está
 * acá lo mostramos lowercase tal cual.
 */
const SLUG_TO_KEY: Record<string, string> = {
  dashboard: "dashboard",
  projects: "projects",
  templates: "templates",
  clients: "clients",
  instances: "instances",
  vms: "vms",
  services: "services",
  routes: "routes",
  cloudflare: "cloudflare",
  monitors: "monitors",
  builds: "builds",
  deployments: "deployments",
  notes: "notes",
  settings: "settings",
  integrations: "integrations",
  domains: "domains",
  environments: "environments",
  "api-keys": "api-keys",
  users: "users",
  roles: "roles",
  notifications: "notifications",
  new: "new",
  edit: "edit",
  records: "records",
  bindings: "bindings",
  rotate: "rotate",
  created: "created",
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

function buildCrumbs(
  pathname: string,
  resolveLabel: (slug: string) => string,
): Crumb[] {
  // Saca el primer "/" y filtra vacíos para "/foo/bar" → ["foo","bar"].
  const segments = pathname.split("/").filter(Boolean);
  if (segments.length === 0) return [];

  // Para que `/projects` no muestre solo "Proyectos" en seco, prependeamos
  // Dashboard como root salvo que ya estemos en /dashboard.
  const crumbs: Crumb[] = [];
  if (segments[0] !== "dashboard") {
    crumbs.push({
      href: "/dashboard",
      label: resolveLabel("dashboard"),
      isId: false,
    });
  }

  let acc = "";
  for (const seg of segments) {
    acc += `/${seg}`;
    const isId = ID_PREFIXES.some((p) => seg.startsWith(p));
    const label = isId ? seg : resolveLabel(seg);
    crumbs.push({ href: acc, label, isId });
  }
  return crumbs;
}

export function Breadcrumbs({ pathname }: { pathname: string }) {
  const t = useTranslations("breadcrumbs");
  const resolveLabel = (slug: string) => {
    const key = SLUG_TO_KEY[slug];
    if (key) {
      try {
        return t(key);
      } catch {
        return decodeURIComponent(slug);
      }
    }
    return decodeURIComponent(slug);
  };
  const crumbs = buildCrumbs(pathname, resolveLabel);
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
