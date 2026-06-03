"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
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
import { ApiError, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  CreateServiceRequest,
  ManagedServiceDetailDto,
  ServiceTemplateDto,
  VmDto,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

export function TemplatePicker({
  templates,
  vms,
}: {
  templates: ServiceTemplateDto[];
  vms: VmDto[];
}) {
  const t = useTranslations("pages.services_new");
  const router = useRouter();
  const [selected, setSelected] = useState<ServiceTemplateDto | null>(null);
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [targetVmId, setTargetVmId] = useState<string>(vms[0]?.id ?? "");
  const [exposedExternally, setExposedExternally] = useState(false);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug) ? null : t("slug_invalid");
  }, [slug, t]);

  function pickTemplate(tpl: ServiceTemplateDto) {
    setSelected(tpl);
    if (!name) setName(tpl.display_name);
    if (!slug) setSlug(suggestSlug(tpl));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selected) return;
    if (slugError) {
      toast.error(slugError);
      return;
    }
    if (!targetVmId) {
      toast.error(t("select_vm_error"));
      return;
    }
    setLoading(true);
    try {
      const body: CreateServiceRequest = {
        template_id: selected.id,
        slug: slug.trim(),
        name: name.trim(),
        target_vm_id: targetVmId,
        exposed_externally: exposedExternally,
      };
      const created = await api<ManagedServiceDetailDto>("/api/services", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created", { name: created.slug }));
      router.push(`/services/${created.id}`);
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

  if (!selected) {
    return (
      <section className="flex flex-col gap-4">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          {t("pick_template_heading")}
        </h2>
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {templates.map((tpl) => (
            <li key={tpl.id}>
              <button
                type="button"
                onClick={() => pickTemplate(tpl)}
                className="w-full text-left"
              >
                <Card className="h-full transition-colors hover:border-primary/40">
                  <CardContent className="flex h-full flex-col gap-3 p-5">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <h3 className="truncate text-base font-semibold text-foreground">
                          {tpl.display_name}
                        </h3>
                        <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                          {tpl.type}
                        </p>
                      </div>
                      <Badge variant="outline" className="font-mono text-[10px]">
                        v{tpl.version}
                      </Badge>
                    </div>
                    <p className="line-clamp-3 text-xs text-muted-foreground">
                      {tpl.notes || t("template_no_notes")}
                    </p>
                    <div className="mt-auto flex items-center justify-between font-mono text-[11px] text-muted-foreground">
                      <span>{tpl.image}</span>
                      <span>:{tpl.internal_port}</span>
                    </div>
                  </CardContent>
                </Card>
              </button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <section className="flex flex-col gap-4">
      <Card>
        <CardContent className="flex items-center justify-between gap-3 p-4">
          <div className="min-w-0">
            <div className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
              {t("section_template_label")}
            </div>
            <div className="flex items-center gap-2">
              <span className="text-sm font-semibold text-foreground">
                {selected.display_name}
              </span>
              <Badge variant="outline" className="font-mono text-[10px]">
                v{selected.version}
              </Badge>
            </div>
            <div className="mt-1 font-mono text-[11px] text-muted-foreground">
              {selected.image}:{selected.internal_port}
            </div>
          </div>
          <Button variant="outline" size="sm" onClick={() => setSelected(null)}>
            {t("change_template")}
          </Button>
        </CardContent>
      </Card>

      <form onSubmit={onSubmit}>
        <Card>
          <CardContent className="space-y-5 p-6">
            <div className="space-y-2">
              <Label htmlFor="name">{t("label_name")}</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={selected.display_name}
                required
                autoFocus
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="slug">{t("label_slug")}</Label>
              <Input
                id="slug"
                value={slug}
                onChange={(e) => setSlug(e.target.value)}
                placeholder={t("placeholder_slug")}
                className="font-mono text-xs"
                required
                autoComplete="off"
                spellCheck={false}
              />
              {slugError ? (
                <p className="text-xs text-destructive">{slugError}</p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  {t("slug_hint")}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label>{t("label_target_vm")}</Label>
              <Select value={targetVmId} onValueChange={setTargetVmId}>
                <SelectTrigger>
                  <SelectValue placeholder={t("placeholder_target_vm")} />
                </SelectTrigger>
                <SelectContent>
                  {vms.length === 0 ? (
                    <SelectItem value="__none__" disabled>
                      {t("no_vms")}
                    </SelectItem>
                  ) : (
                    vms.map((vm) => (
                      <SelectItem key={vm.id} value={vm.id}>
                        {vm.name} ({vm.slug})
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {t("target_vm_hint")}
              </p>
            </div>

            <div className="flex items-start gap-3 rounded-md border border-border bg-muted/30 p-3">
              <Switch
                id="exposed"
                checked={exposedExternally}
                onCheckedChange={setExposedExternally}
              />
              <div className="flex flex-col gap-1">
                <Label htmlFor="exposed" className="cursor-pointer">
                  {t("label_exposed")}
                </Label>
                <p className="text-xs text-muted-foreground">
                  {t("exposed_hint")}
                </p>
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/services")}
              >
                {t("cancel")}
              </Button>
              <Button
                type="submit"
                disabled={
                  loading || !!slugError || !name || !slug || !targetVmId
                }
              >
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("submit")}
              </Button>
            </div>
          </CardContent>
        </Card>
      </form>
    </section>
  );
}

function suggestSlug(tpl: ServiceTemplateDto): string {
  const base = tpl.type.toLowerCase().replace(/[^a-z0-9-]/g, "-");
  return base.length > 0 && /^[a-z]/.test(base) ? base : `srv-${base}`;
}
