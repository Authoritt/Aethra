import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/app-shell";
import { OnboardingBanner } from "@/components/layout/onboarding-banner";

/**
 * Layout que envuelve toda la zona autenticada (`/dashboard`, `/projects`,
 * `/settings`, ...). Renderea el shell global (sidebar + topbar) y un banner
 * de onboarding pre-fetcheado en el servidor.
 *
 * El `OnboardingBanner` es un Server Component, así que podemos pasarlo como
 * children al `AppShell` (client) sin romper la regla "client no importa
 * server". El client component recibe el JSX ya resuelto.
 */
export default function AuthenticatedLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <AppShell banner={<OnboardingBanner />}>
      {children}
    </AppShell>
  );
}
