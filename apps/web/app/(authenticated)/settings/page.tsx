import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Globe, Key, Lock, Plug2, Settings, User } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

interface MeResponse {
  email: string;
  scopes: string[];
}

async function getMe(): Promise<MeResponse | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

export default async function SettingsPage() {
  const me = await getMe();
  if (!me) {
    redirect("/login");
  }

  const cards: Array<{
    href: string;
    title: string;
    description: string;
    icon: typeof Settings;
    available?: boolean;
    comingSoon?: boolean;
  }> = [
    {
      href: "/settings/api-keys",
      title: "API keys",
      description:
        "Tokens portadores para integrar herramientas externas y agentes con la API de Aethra.",
      icon: Key,
      available: true,
    },
    {
      href: "/settings/integrations",
      title: "Integraciones (credenciales)",
      description:
        "Credenciales centralizadas (Cloudflare, GitHub PAT, SMTP, registries) que otros módulos resuelven por nombre.",
      icon: Plug2,
      available: true,
    },
    {
      href: "/settings/domains",
      title: "Base domain (wildcard)",
      description:
        "FQDN bajo el cual Aethra construye los hostnames. Solo uno activo a la vez, con flag de wildcard DNS confirmado.",
      icon: Globe,
      available: true,
    },
    {
      href: "/settings/environments",
      title: "Ambientes",
      description:
        "Catálogo de ambientes válidos (production, staging, preview...). Otros módulos validan slugs contra esta lista.",
      icon: Settings,
      available: true,
    },
    {
      href: "/settings",
      title: "Perfil",
      description: "Email, nombre y preferencias de cuenta.",
      icon: User,
      comingSoon: true,
    },
    {
      href: "/settings",
      title: "DataProtection key",
      description:
        "Llave maestra que cifra tokens en reposo. Rotación controlada.",
      icon: Lock,
      comingSoon: true,
    },
  ];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title="Settings"
        description="Configuración de tu cuenta, credenciales y secretos del workspace."
      />

      <section className="mb-8 grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
        {cards.map((c) => {
          const Inner = (
            <Card
              className={cn(
                "h-full transition-colors",
                c.comingSoon
                  ? "opacity-60"
                  : "hover:border-primary/40",
              )}
            >
              <CardHeader>
                <div className="flex items-start justify-between gap-2">
                  <div className="flex h-9 w-9 items-center justify-center rounded-md bg-muted text-muted-foreground">
                    <c.icon className="h-4 w-4" />
                  </div>
                  {c.available ? <Badge variant="success">Activo</Badge> : null}
                  {c.comingSoon ? <Badge variant="outline">Pronto</Badge> : null}
                </div>
                <CardTitle className="text-base">{c.title}</CardTitle>
                <CardDescription>{c.description}</CardDescription>
              </CardHeader>
            </Card>
          );
          if (c.comingSoon) {
            return (
              <div key={c.title}>{Inner}</div>
            );
          }
          return (
            <Link key={c.title} href={c.href} className="block">
              {Inner}
            </Link>
          );
        })}
      </section>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
            Sesión actual
          </CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
            <div>
              <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Email
              </dt>
              <dd className="mt-0.5 font-mono">{me.email}</dd>
            </div>
            <div>
              <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Scopes
              </dt>
              <dd className="mt-0.5 flex flex-wrap gap-1">
                {me.scopes.length === 0 ? (
                  <span className="text-muted-foreground">(sin scopes)</span>
                ) : (
                  me.scopes.map((s) => (
                    <Badge key={s} variant="outline" className="font-mono text-[10px]">
                      {s}
                    </Badge>
                  ))
                )}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>
    </div>
  );
}
