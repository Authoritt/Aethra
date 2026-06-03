import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Bell, Globe, Key, Lock, Plug2, Settings, User, Users } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

interface MeResponse {
  email: string;
  displayName: string | null;
  roles: string[];
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
  const t = await getTranslations("pages.settings");
  const tCommon = await getTranslations("common");

  const cards: Array<{
    href: string;
    title: string;
    description: string;
    icon: typeof Settings;
    available?: boolean;
    comingSoon?: boolean;
  }> = [
    {
      href: "/settings/users",
      title: t("users_title"),
      description: t("users_description"),
      icon: Users,
      available: true,
    },
    {
      href: "/settings/api-keys",
      title: t("api_keys_title"),
      description: t("api_keys_description"),
      icon: Key,
      available: true,
    },
    {
      href: "/settings/integrations",
      title: t("integrations_title"),
      description: t("integrations_description"),
      icon: Plug2,
      available: true,
    },
    {
      href: "/settings/domains",
      title: t("domains_title"),
      description: t("domains_description"),
      icon: Globe,
      available: true,
    },
    {
      href: "/settings/environments",
      title: t("environments_title"),
      description: t("environments_description"),
      icon: Settings,
      available: true,
    },
    {
      href: "/settings/notifications",
      title: t("notifications_title"),
      description: t("notifications_description"),
      icon: Bell,
      available: true,
    },
    {
      href: "/settings",
      title: t("profile_title"),
      description: t("profile_description"),
      icon: User,
      comingSoon: true,
    },
    {
      href: "/settings",
      title: t("dpkey_title"),
      description: t("dpkey_description"),
      icon: Lock,
      comingSoon: true,
    },
  ];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader title={t("title")} description={t("description")} />

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
                  {c.available ? (
                    <Badge variant="success">{tCommon("active")}</Badge>
                  ) : null}
                  {c.comingSoon ? (
                    <Badge variant="outline">{tCommon("coming_soon")}</Badge>
                  ) : null}
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
            {t("current_session")}
          </CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
            <div>
              <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                {t("user_label")}
              </dt>
              <dd className="mt-0.5">
                <div className="font-medium">{me.displayName ?? me.email}</div>
                <div className="font-mono text-[11px] text-muted-foreground">
                  {me.email}
                </div>
              </dd>
            </div>
            <div>
              <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                {t("roles_label")}
              </dt>
              <dd className="mt-0.5 flex flex-wrap gap-1">
                {me.roles.length === 0 ? (
                  <span className="text-muted-foreground">{t("no_roles")}</span>
                ) : (
                  me.roles.map((r) => (
                    <Badge
                      key={r}
                      variant={r === "admin" ? "warning" : "outline"}
                      className="text-[10px]"
                    >
                      {r}
                    </Badge>
                  ))
                )}
              </dd>
            </div>
            <div className="md:col-span-2">
              <dt className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                {t("scopes_label", { count: me.scopes.length })}
              </dt>
              <dd className="mt-0.5 flex flex-wrap gap-1">
                {me.scopes.length === 0 ? (
                  <span className="text-muted-foreground">{t("no_scopes")}</span>
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
