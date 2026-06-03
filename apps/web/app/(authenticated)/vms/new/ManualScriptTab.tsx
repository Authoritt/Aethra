"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Copy, Loader2, RefreshCw, Terminal } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ApiError, api } from "@/lib/api";
import type { InstallScriptResponse } from "@/lib/types";

interface Props {
  vmId: string;
  initialToken: string;
}

/**
 * Tab 3 — Comando manual. Muestra el bash one-liner para correr en la VM:
 * `curl -fsSL .../install-satellite.sh | sudo bash -s -- --central-url X --token Y --runtime docker`.
 *
 * Rota el token cada vez que se solicita el script (el server lo hace en GetInstallScriptQuery).
 * Útil para VMs detrás de NAT/CGNAT donde Aethra no puede SSH-ear.
 */
export function ManualScriptTab({ vmId, initialToken }: Props) {
  const t = useTranslations("pages.vms_new.manual");
  const tCommon = useTranslations("pages.vms_new");
  const [runtime, setRuntime] = useState<"docker" | "podman">("docker");
  const [installRuntime, setInstallRuntime] = useState(false);
  const [script, setScript] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [token, setToken] = useState<string>(initialToken);

  // Auto-load del script al montar (con runtime default docker).
  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vmId]);

  async function refresh() {
    setLoading(true);
    try {
      const params = new URLSearchParams({
        container_runtime: runtime,
        install_container_runtime: installRuntime ? "true" : "false",
      });
      const response = await api<InstallScriptResponse>(
        `/api/vms/${vmId}/install-script?${params.toString()}`,
      );
      setScript(response.script);
      setToken(response.tokenPlaintext);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : tCommon("error_unknown");
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  async function copy(value: string, label: string) {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(t("copied_toast", { label }));
    } catch {
      toast.error(t("copy_failed"));
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card className="border-info/30 bg-info/5">
        <CardContent className="flex items-start gap-3 p-4 text-sm">
          <Terminal className="mt-0.5 h-4 w-4 shrink-0 text-info" />
          <div>
            <p className="font-medium">{t("header_info_title")}</p>
            <p className="mt-1 text-muted-foreground">
              {t("header_info_description")}
            </p>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="grid grid-cols-1 gap-4 p-6 md:grid-cols-3 md:items-end">
          <div className="space-y-2">
            <Label>{t("label_runtime")}</Label>
            <Select
              value={runtime}
              onValueChange={(v) => setRuntime(v as "docker" | "podman")}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="docker">Docker</SelectItem>
                <SelectItem value="podman">Podman</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center gap-2 pt-7">
            <Checkbox
              id="install-runtime"
              checked={installRuntime}
              onCheckedChange={(v) => setInstallRuntime(v === true)}
            />
            <Label htmlFor="install-runtime" className="m-0 font-normal">
              {t("label_install_runtime")}
            </Label>
          </div>

          <div className="flex justify-end">
            <Button variant="outline" onClick={refresh} disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="mr-2 h-4 w-4" />
              )}
              {t("regenerate")}
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {t("command_label")}
          </CardTitle>
          {script ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => copy(script, t("script_label"))}
            >
              <Copy className="mr-2 h-4 w-4" />
              {t("command_copy")}
            </Button>
          ) : null}
        </CardHeader>
        <CardContent>
          <pre className="overflow-x-auto whitespace-pre-wrap rounded-md border border-border bg-muted px-3 py-2 font-mono text-xs leading-relaxed text-foreground">
            {script ?? t("command_loading")}
          </pre>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {t("token_label")}
          </CardTitle>
          <Button
            variant="outline"
            size="sm"
            onClick={() => copy(token, t("token_copy_label"))}
          >
            <Copy className="mr-2 h-4 w-4" />
            {t("token_copy")}
          </Button>
        </CardHeader>
        <CardContent>
          <pre className="overflow-x-auto whitespace-nowrap rounded-md border border-border bg-muted px-3 py-2 font-mono text-xs text-foreground">
            {token}
          </pre>
        </CardContent>
      </Card>

      <p className="text-xs text-muted-foreground">
        {t("tip")}
      </p>
    </div>
  );
}
