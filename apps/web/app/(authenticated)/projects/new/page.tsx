"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { z } from "zod";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/layout/page-header";
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type { CreateProjectV2Request, ProjectDetailV2 } from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

const COLOR_SWATCHES = [
  "#10b981",
  "#06b6d4",
  "#3b82f6",
  "#6366f1",
  "#a855f7",
  "#ec4899",
  "#f43f5e",
  "#f97316",
  "#facc15",
  "#84cc16",
  "#22c55e",
  "#14b8a6",
];

const ICON_OPTIONS = [
  "cube",
  "folder",
  "rocket",
  "globe",
  "server",
  "database",
  "spark",
  "shield",
  "flame",
  "leaf",
];

type FormValues = {
  slug: string;
  name: string;
  description?: string;
  color: string;
  icon: string;
};

export default function NewProjectPage() {
  const t = useTranslations("pages.projects_new");
  const tBreadcrumbs = useTranslations("breadcrumbs");
  const tValidation = useTranslations("forms.validation");
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const schema = z.object({
    slug: z
      .string()
      .min(1, tValidation("required"))
      .regex(SLUG_RE, tValidation("slug_invalid")),
    name: z.string().min(1, tValidation("required")),
    description: z.string().optional(),
    color: z.string().min(1),
    icon: z.string().min(1),
  });

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      slug: "",
      name: "",
      description: "",
      color: COLOR_SWATCHES[0],
      icon: ICON_OPTIONS[0],
    },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const body: CreateProjectV2Request = {
        slug: values.slug,
        name: values.name.trim(),
        description: values.description?.trim() || null,
        color: values.color,
        icon: values.icon,
      };
      const response = await api<ProjectDetailV2>("/api/projects", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created", { name: response.name }));
      router.push(`/projects/${response.id}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? t("error_status", { status: e.status })
          : e instanceof Error
            ? e.message
            : t("error_unknown");
      toast.error(msg);
    } finally {
      setSubmitting(false);
    }
  }

  const color = form.watch("color");

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("projects"), href: "/projects" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <Form {...form}>
            <form
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-col gap-5"
            >
              <FormField
                control={form.control}
                name="slug"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_slug")}</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        onChange={(e) =>
                          field.onChange(e.target.value.toLowerCase())
                        }
                        placeholder={t("placeholder_slug")}
                        className="font-mono text-xs"
                        maxLength={31}
                      />
                    </FormControl>
                    <FormDescription>{t("help_slug")}</FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_name")}</FormLabel>
                    <FormControl>
                      <Input {...field} placeholder={t("placeholder_name")} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="description"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_description")}</FormLabel>
                    <FormControl>
                      <Textarea
                        {...field}
                        rows={3}
                        placeholder={t("placeholder_description")}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="color"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_color")}</FormLabel>
                    <div className="flex flex-wrap items-center gap-2">
                      {COLOR_SWATCHES.map((c) => (
                        <button
                          key={c}
                          type="button"
                          onClick={() => field.onChange(c)}
                          aria-label={t("color_aria", { color: c })}
                          className={cn(
                            "size-7 rounded-full border-2 transition",
                            field.value === c
                              ? "border-foreground ring-2 ring-ring/40"
                              : "border-border hover:border-foreground/40",
                          )}
                          style={{ backgroundColor: c }}
                        />
                      ))}
                      <input
                        type="color"
                        value={field.value}
                        onChange={(e) => field.onChange(e.target.value)}
                        className="h-7 w-10 cursor-pointer rounded-md border border-input bg-background"
                        aria-label={t("color_custom_aria")}
                      />
                      <span className="font-mono text-xs text-muted-foreground">
                        {color}
                      </span>
                    </div>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="icon"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_icon")}</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {ICON_OPTIONS.map((i) => (
                          <SelectItem key={i} value={i}>
                            {i}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <div className="flex justify-end gap-2 pt-2">
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => router.push("/projects")}
                >
                  {t("cancel")}
                </Button>
                <Button type="submit" disabled={submitting}>
                  {submitting ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : null}
                  {t("submit")}
                </Button>
              </div>
            </form>
          </Form>
        </CardContent>
      </Card>
    </div>
  );
}
