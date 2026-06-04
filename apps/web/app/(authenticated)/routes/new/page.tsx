"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { AlertTriangle, Loader2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type { CreateRouteRequest, RouteDto } from "@/lib/types";

const FQDN_RE =
  /^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/i;
const BACKEND_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;

export default function NewRoutePage() {
  const t = useTranslations("pages.routes_new");
  const tBreadcrumbs = useTranslations("breadcrumbs");
  const tValidation = useTranslations("forms.validation");
  const router = useRouter();
  const [hostname, setHostname] = useState("");
  const [backendUrl, setBackendUrl] = useState("");
  const [tlsEnabled, setTlsEnabled] = useState(true);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(hostname.trim())) return tValidation("url_invalid");
    if (!BACKEND_RE.test(backendUrl.trim())) return tValidation("url_invalid");
    return null;
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    const v = validate();
    if (v) {
      toast.error(v);
      return;
    }
    setLoading(true);
    try {
      const body: CreateRouteRequest = {
        hostname: hostname.trim(),
        backendUrl: backendUrl.trim(),
        tlsEnabled: tlsEnabled,
      };
      await api<RouteDto>("/api/proxy/routes", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created", { hostname: hostname.trim() }));
      router.push("/routes");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : t("error_unknown");
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("routes"), href: "/routes" },
          { label: tBreadcrumbs("new") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label htmlFor="hostname">{t("label_hostname")}</Label>
              <Input
                id="hostname"
                value={hostname}
                onChange={(e) => setHostname(e.target.value)}
                placeholder={t("placeholder_hostname")}
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="backend">{t("label_backend")}</Label>
              <Input
                id="backend"
                value={backendUrl}
                onChange={(e) => setBackendUrl(e.target.value)}
                placeholder={t("placeholder_backend")}
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
            </div>

            <div className="flex items-start gap-3 rounded-md border border-border bg-muted/30 p-3">
              <Switch
                id="tls"
                checked={tlsEnabled}
                onCheckedChange={setTlsEnabled}
              />
              <div>
                <Label htmlFor="tls" className="cursor-pointer">
                  {t("label_tls")}
                </Label>
                <p className="text-xs text-muted-foreground">
                  {t("help_tls")}
                </p>
              </div>
            </div>

            {tlsEnabled ? (
              <Card className="border-warning/30 bg-warning/5">
                <CardContent className="flex items-start gap-3 p-3">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
                  <p className="text-xs text-muted-foreground">
                    {t("help_tls")}
                  </p>
                </CardContent>
              </Card>
            ) : null}

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/routes")}
              >
                {t("cancel")}
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("submit")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
