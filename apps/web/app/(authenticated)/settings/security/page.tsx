import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { ShieldCheck } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import { TwoFactorPanel } from "./TwoFactorPanel";

export const dynamic = "force-dynamic";

interface MeResponse {
  email: string;
  displayName: string | null;
  roles: string[];
  scopes: string[];
  totp_enabled: boolean | null;
  totp_recovery_codes_remaining: number | null;
}

async function fetchMe(): Promise<MeResponse | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401 || res.status === 403) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as MeResponse;
}

export default async function SecurityPage() {
  const data = await fetchMe();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const me = errored ? null : (data as MeResponse);

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Security" },
        ]}
        title="Security"
        description="Manage your account security settings."
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            Failed to load your account settings.
          </CardContent>
        </Card>
      ) : me ? (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-sm font-medium uppercase tracking-wider text-muted-foreground">
              <ShieldCheck className="h-4 w-4" />
              Two-factor authentication
            </CardTitle>
          </CardHeader>
          <CardContent>
            <TwoFactorPanel
              email={me.email}
              initiallyEnabled={Boolean(me.totp_enabled)}
              recoveryCodesRemaining={me.totp_recovery_codes_remaining}
            />
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}
