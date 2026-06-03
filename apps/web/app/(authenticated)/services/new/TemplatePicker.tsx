"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import {
  Activity,
  Boxes,
  Database,
  FileText,
  Loader2,
  Network,
  Package,
  Search,
  Server,
  Workflow,
  X,
  Zap,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
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

// Categorías canónicas que el backend emite (TemplateCategories). El "all" es un filtro de UI.
const KNOWN_CATEGORIES = [
  "Database",
  "Messaging",
  "Storage",
  "CMS",
  "Analytics",
  "Automation",
  "Search",
  "Other",
] as const;

const CATEGORY_ICONS: Record<string, LucideIcon> = {
  Database: Database,
  Messaging: Network,
  Storage: Boxes,
  CMS: FileText,
  Analytics: Activity,
  Automation: Workflow,
  Search: Search,
  Other: Package,
};

function categoryIcon(category: string): LucideIcon {
  return CATEGORY_ICONS[category] ?? Server;
}

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

  const [query, setQuery] = useState("");
  const [activeCategory, setActiveCategory] = useState<string>("all");

  // Sólo mostramos chips para categorías presentes en el catálogo + ALL al principio.
  const availableCategories = useMemo(() => {
    const present = new Set(templates.map((tpl) => tpl.category ?? "Other"));
    return KNOWN_CATEGORIES.filter((c) => present.has(c));
  }, [templates]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return templates.filter((tpl) => {
      if (activeCategory !== "all" && (tpl.category ?? "Other") !== activeCategory) {
        return false;
      }
      if (q.length === 0) return true;
      const haystack = [
        tpl.display_name,
        tpl.type,
        tpl.category ?? "",
        tpl.description ?? "",
        tpl.notes ?? "",
        ...(tpl.tags ?? []),
      ]
        .join(" ")
        .toLowerCase();
      return haystack.includes(q);
    });
  }, [templates, query, activeCategory]);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug) ? null : t("slug_invalid");
  }, [slug, t]);

  function pickTemplate(tpl: ServiceTemplateDto) {
    setSelected(tpl);
    if (!name) setName(tpl.display_name);
    if (!slug) setSlug(suggestSlug(tpl));
  }

  function clearFilters() {
    setQuery("");
    setActiveCategory("all");
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
      <section className="flex flex-col gap-5">
        <h2 className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          {t("pick_template_heading")}
        </h2>

        {/* Search + clear */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t("search_placeholder")}
              className="pl-9"
              autoComplete="off"
              spellCheck={false}
            />
          </div>
          {(query || activeCategory !== "all") && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={clearFilters}
              className="self-start sm:self-auto"
            >
              <X className="mr-1.5 h-3.5 w-3.5" />
              {t("clear_filters")}
            </Button>
          )}
        </div>

        {/* Category chips */}
        <div className="flex flex-wrap gap-2">
          <CategoryChip
            label={t("category_all")}
            icon={Zap}
            active={activeCategory === "all"}
            onClick={() => setActiveCategory("all")}
          />
          {availableCategories.map((cat) => {
            const Icon = categoryIcon(cat);
            return (
              <CategoryChip
                key={cat}
                label={categoryLabel(t, cat)}
                icon={Icon}
                active={activeCategory === cat}
                onClick={() => setActiveCategory(cat)}
              />
            );
          })}
        </div>

        {/* Grid */}
        {filtered.length === 0 ? (
          <Card className="border-dashed">
            <CardContent className="flex flex-col items-center justify-center gap-3 p-8 text-center">
              <Search className="h-8 w-8 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">{t("no_results")}</p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={clearFilters}
              >
                {t("clear_filters")}
              </Button>
            </CardContent>
          </Card>
        ) : (
          <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {filtered.map((tpl) => (
              <li key={tpl.id}>
                <TemplateCard tpl={tpl} onPick={() => pickTemplate(tpl)} t={t} />
              </li>
            ))}
          </ul>
        )}
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

function CategoryChip({
  label,
  icon: Icon,
  active,
  onClick,
}: {
  label: string;
  icon: LucideIcon;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors",
        active
          ? "border-primary bg-primary text-primary-foreground"
          : "border-border bg-background text-foreground hover:bg-muted",
      )}
    >
      <Icon className="h-3.5 w-3.5" />
      {label}
    </button>
  );
}

function TemplateCard({
  tpl,
  onPick,
  t,
}: {
  tpl: ServiceTemplateDto;
  onPick: () => void;
  t: ReturnType<typeof useTranslations>;
}) {
  const Icon = categoryIcon(tpl.category ?? "Other");
  const desc = tpl.description ?? tpl.notes ?? "";
  const tags = (tpl.tags ?? []).slice(0, 3);
  const deps = tpl.dependencies ?? [];

  return (
    <button type="button" onClick={onPick} className="block w-full text-left">
      <Card className="h-full transition-colors hover:border-primary/40 hover:shadow-sm">
        <CardContent className="flex h-full flex-col gap-3 p-5">
          <div className="flex items-start gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center overflow-hidden rounded-md border border-border bg-muted/40">
              {tpl.icon_url ? (
                /* eslint-disable-next-line @next/next/no-img-element */
                <img
                  src={tpl.icon_url}
                  alt={`${tpl.display_name} logo`}
                  width={28}
                  height={28}
                  loading="lazy"
                  className="h-7 w-7 object-contain"
                />
              ) : (
                <Icon className="h-5 w-5 text-muted-foreground" />
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-1.5">
                <h3 className="truncate text-sm font-semibold text-foreground">
                  {tpl.display_name}
                </h3>
                <Badge variant="outline" className="font-mono text-[10px]">
                  v{tpl.version}
                </Badge>
              </div>
              <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                {tpl.category ?? tpl.type}
              </p>
            </div>
          </div>

          {desc ? (
            <p className="line-clamp-3 text-xs text-muted-foreground">{desc}</p>
          ) : null}

          <div className="mt-auto flex flex-col gap-2">
            {deps.length > 0 ? (
              <div className="flex flex-wrap items-center gap-1 text-[10px] text-muted-foreground">
                <span className="font-medium uppercase tracking-wider">
                  {t("requires_label")}:
                </span>
                {deps.map((dep) => (
                  <Badge
                    key={dep}
                    variant="secondary"
                    className="font-mono text-[10px]"
                  >
                    {dep}
                  </Badge>
                ))}
              </div>
            ) : null}

            <div className="flex flex-wrap items-center gap-1">
              {tpl.binding_supported ? (
                <Badge variant="info" className="text-[10px]">
                  {t("binding_supported_badge")}
                </Badge>
              ) : null}
              {tpl.multi_container ? (
                <Badge variant="warning" className="text-[10px]">
                  {t("multi_container_badge")}
                </Badge>
              ) : null}
              {tags.map((tag) => (
                <Badge
                  key={tag}
                  variant="outline"
                  className="font-mono text-[10px] lowercase"
                >
                  #{tag}
                </Badge>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>
    </button>
  );
}

function suggestSlug(tpl: ServiceTemplateDto): string {
  // Preferimos el template id (mysql-8, mongo-7…) si es válido como slug; sino caemos al type.
  const baseRaw = tpl.id || tpl.type;
  const base = baseRaw.toLowerCase().replace(/[^a-z0-9-]/g, "-");
  return base.length > 0 && /^[a-z]/.test(base) ? base : `srv-${base}`;
}

// next-intl no expone defaultValue en t(); resolvemos manualmente y caemos al nombre crudo si
// algún día llega una categoría que aún no tiene entrada i18n.
function categoryLabel(
  t: ReturnType<typeof useTranslations>,
  category: string,
): string {
  const knownKey = `category_${category}`;
  type CategoryKey =
    | "category_Database"
    | "category_Messaging"
    | "category_Storage"
    | "category_CMS"
    | "category_Analytics"
    | "category_Automation"
    | "category_Search"
    | "category_Other";
  const allowed: ReadonlyArray<string> = [
    "category_Database",
    "category_Messaging",
    "category_Storage",
    "category_CMS",
    "category_Analytics",
    "category_Automation",
    "category_Search",
    "category_Other",
  ];
  return allowed.includes(knownKey) ? t(knownKey as CategoryKey) : category;
}
