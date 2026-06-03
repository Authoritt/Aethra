"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  Activity,
  Bell,
  Boxes,
  Cloud,
  Database,
  FileCode,
  FolderKanban,
  Hammer,
  LayoutDashboard,
  Network,
  Rocket,
  Server,
  Settings,
  Users,
  type LucideIcon,
} from "lucide-react";
import { Logo } from "@/components/brand/logo";
import { cn } from "@/lib/utils";

interface NavItem {
  href: string;
  /** Key dentro de `nav.*` en messages/{locale}.json. */
  labelKey: string;
  icon: LucideIcon;
}

interface NavGroup {
  /** Key dentro de `nav.*` en messages/{locale}.json. */
  labelKey: string;
  items: NavItem[];
}

/**
 * Estructura del menú lateral. Las strings concretas viven en `messages/`,
 * acá solo declaramos las keys para que `useTranslations("nav")` las resuelva.
 */
const navGroups: NavGroup[] = [
  {
    labelKey: "group_operation",
    items: [
      { href: "/dashboard", labelKey: "dashboard", icon: LayoutDashboard },
      { href: "/projects", labelKey: "projects", icon: FolderKanban },
      { href: "/templates", labelKey: "templates", icon: FileCode },
      { href: "/clients", labelKey: "clients", icon: Users },
      { href: "/instances", labelKey: "instances", icon: Boxes },
      { href: "/builds", labelKey: "builds", icon: Hammer },
      { href: "/deployments", labelKey: "deployments", icon: Rocket },
      { href: "/monitors", labelKey: "monitors", icon: Activity },
    ],
  },
  {
    labelKey: "group_infrastructure",
    items: [
      { href: "/vms", labelKey: "vms", icon: Server },
      { href: "/routes", labelKey: "routes", icon: Network },
      { href: "/services", labelKey: "services", icon: Database },
      { href: "/cloudflare", labelKey: "cloudflare", icon: Cloud },
    ],
  },
  {
    labelKey: "group_configuration",
    items: [
      { href: "/settings", labelKey: "settings", icon: Settings },
      { href: "/settings/users", labelKey: "users", icon: Users },
      { href: "/settings/notifications", labelKey: "notifications", icon: Bell },
    ],
  },
];

/**
 * Calcula si un item está "activo" para el pathname dado.
 * - `/dashboard` → activo solo en pathname exacto (`/dashboard`).
 * - El resto matchean con startsWith para que `/projects/prj_xxx` mantenga
 *   "Proyectos" resaltado.
 */
function isItemActive(itemHref: string, pathname: string): boolean {
  if (itemHref === "/dashboard") return pathname === "/dashboard";
  return pathname === itemHref || pathname.startsWith(`${itemHref}/`);
}

export interface AppSidebarProps {
  /**
   * Callback que el sidebar invoca cuando el usuario navega. Se usa para
   * cerrar el Sheet móvil al clickear un link.
   */
  onNavigate?: () => void;
}

export function AppSidebar({ onNavigate }: AppSidebarProps) {
  const pathname = usePathname();
  const t = useTranslations("nav");

  // Por ahora hardcodeado — F11 lo conectará con la variable de entorno
  // ASPNETCORE_ENVIRONMENT o un endpoint /context que ya existe.
  const isProd = process.env.NEXT_PUBLIC_ENV === "production";
  const envLabel = isProd ? t("env_production") : t("env_development");

  return (
    <aside className="flex h-full w-full flex-col bg-card text-card-foreground">
      <div className="px-4 py-5">
        <Link
          href="/dashboard"
          onClick={onNavigate}
          className="inline-flex rounded-md outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <Logo variant="lockup" size={26} />
        </Link>
      </div>

      <nav className="flex-1 overflow-y-auto px-3 pb-4">
        {navGroups.map((group) => (
          <div key={group.labelKey} className="mb-4 last:mb-0">
            <div className="px-3 py-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              {t(group.labelKey)}
            </div>
            <ul className="flex flex-col gap-0.5">
              {group.items.map((item) => {
                const active = isItemActive(item.href, pathname);
                const Icon = item.icon;
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={onNavigate}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "flex items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring",
                        active
                          ? "bg-secondary text-foreground"
                          : "text-muted-foreground hover:bg-secondary/50 hover:text-foreground",
                      )}
                    >
                      <Icon
                        className={cn(
                          "size-4 shrink-0",
                          active ? "text-primary" : "text-muted-foreground",
                        )}
                        aria-hidden="true"
                      />
                      <span className="truncate">{t(item.labelKey)}</span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>

      <div className="border-t border-border px-4 py-3">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <span
            className={cn(
              "size-1.5 rounded-full",
              isProd ? "bg-emerald-500" : "bg-amber-500",
            )}
            aria-hidden="true"
          />
          <span>{envLabel}</span>
        </div>
      </div>
    </aside>
  );
}
