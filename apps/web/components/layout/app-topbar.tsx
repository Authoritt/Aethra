"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import Link from "next/link";
import { useTheme } from "next-themes";
import {
  LogOut,
  Menu,
  Monitor,
  Moon,
  Palette,
  Sun,
  User,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
} from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { API_URL } from "@/lib/api";
import { Breadcrumbs } from "./breadcrumbs";

interface AppTopbarProps {
  /** Callback que el shell pasa para abrir el sidebar móvil. */
  onOpenSidebar: () => void;
}

interface MeResponse {
  email: string;
  displayName: string | null;
  roles: string[];
  scopes: string[];
}

const FALLBACK_EMAIL = "admin@aethra.local";

export function AppTopbar({ onOpenSidebar }: AppTopbarProps) {
  const pathname = usePathname();
  const router = useRouter();
  const [me, setMe] = useState<MeResponse | null>(null);
  const [loadingMe, setLoadingMe] = useState(true);

  useEffect(() => {
    let cancelled = false;
    fetch(`${API_URL}/auth/me`, { credentials: "include" })
      .then((res) => (res.ok ? (res.json() as Promise<MeResponse>) : null))
      .then((data) => {
        if (cancelled) return;
        setMe(data);
        setLoadingMe(false);
      })
      .catch(() => {
        if (cancelled) return;
        setMe(null);
        setLoadingMe(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const email = me?.email ?? FALLBACK_EMAIL;
  const displayName = me?.displayName ?? null;
  const roles = me?.roles ?? [];
  const initial = ((displayName ?? email) || "A").charAt(0).toUpperCase();

  async function handleLogout() {
    try {
      await fetch(`${API_URL}/auth/logout`, {
        method: "POST",
        credentials: "include",
      });
    } catch {
      // Aunque falle, redirigimos: el server invalida el cookie en su próxima request.
    }
    router.push("/login");
  }

  return (
    <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-3 border-b border-border bg-background/80 px-3 backdrop-blur-md md:px-6">
      <Button
        variant="ghost"
        size="icon"
        className="md:hidden"
        onClick={onOpenSidebar}
        aria-label="Abrir menú"
      >
        <Menu className="size-5" />
      </Button>

      <div className="min-w-0 flex-1">
        <Breadcrumbs pathname={pathname} />
      </div>

      <div className="flex items-center gap-1">
        <ThemeToggle />
        <UserMenu
          email={loadingMe ? "Cargando..." : email}
          displayName={displayName}
          roles={roles}
          initial={initial}
          onLogout={handleLogout}
        />
      </div>
    </header>
  );
}

function ThemeToggle() {
  const { theme, setTheme, resolvedTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  // next-themes recomienda gate-ar el render hasta mounted para evitar mismatch.
  useEffect(() => setMounted(true), []);

  const current = mounted ? (theme ?? "system") : "system";
  const effective = mounted ? (resolvedTheme ?? "light") : "light";

  // Icono que mostramos en el trigger: refleja el tema efectivo, no la
  // configuración (si está "system" mostramos lo que está aplicado).
  const TriggerIcon =
    current === "branded"
      ? Palette
      : current === "system"
        ? Monitor
        : effective === "dark"
          ? Moon
          : Sun;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Cambiar tema">
          <TriggerIcon className="size-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
          Tema
        </DropdownMenuLabel>
        <DropdownMenuItem onSelect={() => setTheme("light")}>
          <Sun className="size-4" />
          Claro
          {current === "light" && (
            <span className="ml-auto text-xs text-muted-foreground">●</span>
          )}
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => setTheme("dark")}>
          <Moon className="size-4" />
          Oscuro
          {current === "dark" && (
            <span className="ml-auto text-xs text-muted-foreground">●</span>
          )}
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => setTheme("branded")}>
          <Palette className="size-4" />
          Branded
          {current === "branded" && (
            <span className="ml-auto text-xs text-muted-foreground">●</span>
          )}
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => setTheme("system")}>
          <Monitor className="size-4" />
          Sistema
          {current === "system" && (
            <span className="ml-auto text-xs text-muted-foreground">●</span>
          )}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function UserMenu({
  email,
  displayName,
  roles,
  initial,
  onLogout,
}: {
  email: string;
  displayName: string | null;
  roles: string[];
  initial: string;
  onLogout: () => void | Promise<void>;
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="ml-1 inline-flex size-9 items-center justify-center rounded-full outline-none ring-offset-background transition focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          aria-label="Menú de usuario"
        >
          <Avatar className="size-9">
            <AvatarImage src="" alt="" />
            <AvatarFallback className="bg-primary/10 text-primary">
              {initial}
            </AvatarFallback>
          </Avatar>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuLabel>
          <div className="flex flex-col gap-1">
            <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Sesión
            </span>
            <span className="truncate text-sm font-medium text-foreground">
              {displayName ?? email}
            </span>
            {displayName ? (
              <span className="truncate text-[11px] text-muted-foreground">
                {email}
              </span>
            ) : null}
            {roles.length > 0 ? (
              <div className="mt-1 flex flex-wrap gap-1">
                {roles.map((r) => (
                  <span
                    key={r}
                    className={
                      "rounded-md border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wider " +
                      (r === "admin"
                        ? "border-amber-500/40 bg-amber-500/10 text-amber-600"
                        : "border-border bg-muted text-muted-foreground")
                    }
                  >
                    {r}
                  </span>
                ))}
              </div>
            ) : null}
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/settings" className="cursor-pointer">
            <User className="size-4" />
            Mi cuenta
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link href="/settings/users" className="cursor-pointer">
            <User className="size-4" />
            Gestión de usuarios
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onSelect={() => void onLogout()}
          className="cursor-pointer text-destructive focus:bg-destructive/10 focus:text-destructive"
        >
          <LogOut className="size-4" />
          Cerrar sesión
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
