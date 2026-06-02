"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname } from "next/navigation";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { AppSidebar } from "./app-sidebar";
import { AppTopbar } from "./app-topbar";

interface AppShellProps {
  children: ReactNode;
  /**
   * Banner que aparece arriba del contenido principal (típicamente un Server
   * Component que pre-fetcha estado de onboarding). Es opcional.
   */
  banner?: ReactNode;
}

/**
 * Layout principal de toda la zona autenticada.
 *
 * - Desktop (`md+`): grid con sidebar fija 240px + main column.
 * - Mobile (`< md`): sidebar oculta; un Sheet la abre desde la izquierda
 *   cuando se toca el botón hamburguesa del topbar.
 *
 * El topbar es sticky y se mantiene visible al scrollear el contenido.
 */
export function AppShell({ children, banner }: AppShellProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const pathname = usePathname();

  // Cerramos el drawer cuando cambia la ruta — el callback `onNavigate` del
  // sidebar también lo hace explícitamente para que se sienta instantáneo,
  // pero este efecto cubre el caso de navegaciones programáticas.
  useEffect(() => {
    setSidebarOpen(false);
  }, [pathname]);

  return (
    <div className="flex min-h-screen w-full bg-background">
      <aside className="hidden w-60 shrink-0 border-r border-border md:block">
        <div className="sticky top-0 h-screen">
          <AppSidebar />
        </div>
      </aside>

      <Sheet open={sidebarOpen} onOpenChange={setSidebarOpen}>
        <SheetContent
          side="left"
          className="w-72 max-w-[85vw] border-r border-border bg-card p-0"
        >
          <AppSidebar onNavigate={() => setSidebarOpen(false)} />
        </SheetContent>
      </Sheet>

      <div className="flex min-w-0 flex-1 flex-col">
        <AppTopbar onOpenSidebar={() => setSidebarOpen(true)} />
        {banner ? <div className="px-3 pt-3 md:px-6 md:pt-4">{banner}</div> : null}
        <main className="flex-1">{children}</main>
      </div>
    </div>
  );
}
