"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type {
  BindingPermissions,
  CreateBindingRequest,
  ServiceBindingDto,
} from "@/lib/types";

export interface ApplicationOption {
  id: string;
  slug: string;
  name: string;
  project_slug: string;
  environment_name: string;
}

type RunOn = "binding_create" | "deploy" | "manual";

export function NewBindingForm({
  serviceId,
  serviceType,
  applications,
}: {
  serviceId: string;
  serviceType: string;
  applications: ApplicationOption[];
}) {
  const t = useTranslations("pages.bindings_new");
  const router = useRouter();
  const [applicationId, setApplicationId] = useState<string>(
    applications[0]?.id ?? "",
  );
  const [resourceName, setResourceName] = useState("");
  const [permissions, setPermissions] = useState<BindingPermissions>("Owner");
  const [envVarPrefix, setEnvVarPrefix] = useState("");

  const [hookEnabled, setHookEnabled] = useState(false);
  const [hookCommand, setHookCommand] = useState("");
  const [hookTimeout, setHookTimeout] = useState(120);
  const [hookFailOnError, setHookFailOnError] = useState(true);
  const [hookRunOn, setHookRunOn] = useState<RunOn>("binding_create");
  const [loading, setLoading] = useState(false);

  const PERMS: { value: BindingPermissions; label: string; hint: string }[] = [
    {
      value: "Owner",
      label: t("perm_owner_label"),
      hint: t("perm_owner_hint"),
    },
    {
      value: "ReadWrite",
      label: t("perm_readwrite_label"),
      hint: t("perm_readwrite_hint"),
    },
    {
      value: "ReadOnly",
      label: t("perm_readonly_label"),
      hint: t("perm_readonly_hint"),
    },
  ];

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!applicationId) {
      toast.error(t("select_app_error"));
      return;
    }
    if (hookEnabled && !hookCommand.trim()) {
      toast.error(t("hook_command_error"));
      return;
    }
    setLoading(true);
    try {
      const body: CreateBindingRequest = {
        application_id: applicationId,
        permissions,
      };
      if (resourceName.trim()) body.resource_name = resourceName.trim();
      if (envVarPrefix.trim()) body.env_var_prefix = envVarPrefix.trim();
      if (hookEnabled) {
        body.migrations_hook = {
          command: hookCommand.trim(),
          timeout_seconds: Number.isFinite(hookTimeout) ? hookTimeout : 120,
          fail_on_error: hookFailOnError,
          run_on: hookRunOn,
        };
      }
      await api<ServiceBindingDto>(`/api/services/${serviceId}/bindings`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created"));
      router.push(`/services/${serviceId}`);
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

  if (applications.length === 0) {
    return (
      <EmptyState
        title={t("no_apps_title")}
        description={t("no_apps_description")}
        action={
          <Button asChild variant="outline">
            <Link href="/projects">{t("go_to_projects")}</Link>
          </Button>
        }
      />
    );
  }

  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardContent className="space-y-5 p-6">
          <div className="space-y-2">
            <Label>{t("label_application")}</Label>
            <Select value={applicationId} onValueChange={setApplicationId}>
              <SelectTrigger>
                <SelectValue placeholder={t("placeholder_application")} />
              </SelectTrigger>
              <SelectContent>
                {applications.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.project_slug} / {a.environment_name} / {a.slug}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="resource">{t("label_resource")}</Label>
            <Input
              id="resource"
              value={resourceName}
              onChange={(e) => setResourceName(e.target.value)}
              placeholder={t("resource_placeholder")}
              autoComplete="off"
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              {t("resource_hint")}
            </p>
          </div>

          <div className="space-y-2">
            <Label>{t("label_permissions")}</Label>
            <div className="flex flex-col gap-2">
              {PERMS.map((p) => (
                <label
                  key={p.value}
                  className={cn(
                    "flex cursor-pointer items-start gap-3 rounded-md border p-3 text-sm transition",
                    permissions === p.value
                      ? "border-primary/40 bg-primary/5"
                      : "border-border bg-muted/30 hover:border-foreground/20",
                  )}
                >
                  <input
                    type="radio"
                    name="permissions"
                    value={p.value}
                    checked={permissions === p.value}
                    onChange={() => setPermissions(p.value)}
                    className="mt-0.5 size-4 accent-primary"
                  />
                  <span className="flex flex-col gap-0.5">
                    <span className="font-medium text-foreground">
                      {p.label}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {p.hint}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="prefix">{t("label_env_prefix")}</Label>
            <Input
              id="prefix"
              value={envVarPrefix}
              onChange={(e) => setEnvVarPrefix(e.target.value)}
              placeholder={t("env_prefix_placeholder")}
              className="font-mono text-xs"
              autoComplete="off"
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              {t("env_prefix_hint")}
            </p>
          </div>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-3">
            <div className="flex items-start gap-3">
              <Switch
                id="hook"
                checked={hookEnabled}
                onCheckedChange={setHookEnabled}
              />
              <div>
                <Label htmlFor="hook" className="cursor-pointer">
                  {t("label_migrations_hook")}
                </Label>
                <p className="text-xs text-muted-foreground">
                  {t("migrations_hook_hint")}
                </p>
              </div>
            </div>

            {hookEnabled ? (
              <div className="space-y-4 pt-2">
                <div className="space-y-2">
                  <Label htmlFor="hookcmd">{t("hook_command_label")}</Label>
                  <Input
                    id="hookcmd"
                    value={hookCommand}
                    onChange={(e) => setHookCommand(e.target.value)}
                    placeholder={t("hook_command_placeholder")}
                    className="font-mono text-xs"
                    autoComplete="off"
                    spellCheck={false}
                  />
                  <p className="text-xs text-muted-foreground">
                    {serviceType.toLowerCase().includes("postgres")
                      ? t("hook_command_hint_postgres")
                      : t("hook_command_hint_default")}
                  </p>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-2">
                    <Label htmlFor="timeout">{t("hook_timeout_label")}</Label>
                    <Input
                      id="timeout"
                      type="number"
                      min={1}
                      max={3600}
                      value={hookTimeout}
                      onChange={(e) =>
                        setHookTimeout(parseInt(e.target.value, 10) || 0)
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>{t("hook_trigger_label")}</Label>
                    <Select
                      value={hookRunOn}
                      onValueChange={(v) => setHookRunOn(v as RunOn)}
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="binding_create">
                          {t("hook_trigger_binding_create")}
                        </SelectItem>
                        <SelectItem value="deploy">{t("hook_trigger_deploy")}</SelectItem>
                        <SelectItem value="manual">{t("hook_trigger_manual")}</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="flex items-start gap-3">
                  <Switch
                    id="failon"
                    checked={hookFailOnError}
                    onCheckedChange={setHookFailOnError}
                  />
                  <div>
                    <Label htmlFor="failon" className="cursor-pointer">
                      {t("hook_fail_on_label")}
                    </Label>
                    <p className="text-xs text-muted-foreground">
                      {t("hook_fail_on_hint")}
                    </p>
                  </div>
                </div>
              </div>
            ) : null}
          </fieldset>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/services/${serviceId}`)}
            >
              {t("cancel")}
            </Button>
            <Button type="submit" disabled={loading || !applicationId}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {t("submit")}
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
  );
}
