"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, Loader2 } from "lucide-react";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ApiError, api } from "@/lib/api";
import type { BaseDomainDto, CreateBaseDomainRequest } from "@/lib/types";

interface CloudflareZoneOption {
  id: string;
  name: string;
}

// FQDN simple: dos o mas labels lowercase alfanumericos con guiones.
const FQDN_RE =
  /^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$/;

const NO_ZONE_VALUE = "__none__";

export function CreateBaseDomainForm({
  zones,
}: {
  zones: CloudflareZoneOption[];
}) {
  const t = useTranslations("pages.settings_domains.new");
  const tParent = useTranslations("pages.settings_domains");
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const schema = useMemo(
    () =>
      z.object({
        hostname: z
          .string()
          .min(1, t("validation_required"))
          .max(253, t("validation_max"))
          .regex(FQDN_RE, t("validation_format")),
        cloudflareZoneId: z.string().optional(),
      }),
    [t],
  );

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      hostname: "",
      cloudflareZoneId: NO_ZONE_VALUE,
    },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const zoneId =
        values.cloudflareZoneId &&
        values.cloudflareZoneId !== NO_ZONE_VALUE &&
        values.cloudflareZoneId.trim().length > 0
          ? values.cloudflareZoneId.trim()
          : null;
      const body: CreateBaseDomainRequest = {
        hostname: values.hostname.trim().toLowerCase(),
        cloudflareZoneId: zoneId,
      };
      const created = await api<BaseDomainDto>("/api/settings/domains/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created"));
      router.push(
        `/settings/domains?created=${encodeURIComponent(created.id)}`,
      );
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : tParent("error_unknown");
      toast.error(msg);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <CardContent className="p-6">
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-5"
          >
            <FormField
              control={form.control}
              name="hostname"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_hostname")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
                      maxLength={253}
                      placeholder={t("placeholder_hostname")}
                      autoComplete="off"
                      spellCheck={false}
                      autoFocus
                    />
                  </FormControl>
                  <FormDescription>
                    {t("hostname_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="cloudflareZoneId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_zone")}</FormLabel>
                  {zones.length === 0 ? (
                    <Card className="border-border">
                      <CardContent className="p-3 text-xs text-muted-foreground">
                        {t("no_zones_message")}{" "}
                        <Link
                          href="/cloudflare/new"
                          className="text-primary underline-offset-4 hover:underline"
                        >
                          {t("no_zones_cta")}
                        </Link>
                        .
                      </CardContent>
                    </Card>
                  ) : (
                    <Select
                      value={field.value || NO_ZONE_VALUE}
                      onValueChange={field.onChange}
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue placeholder={t("zone_unlinked")} />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        <SelectItem value={NO_ZONE_VALUE}>
                          {t("zone_unlinked")}
                        </SelectItem>
                        {zones.map((z) => (
                          <SelectItem key={z.id} value={z.id}>
                            {z.name} ({z.id.slice(0, 12)}...)
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                  <FormDescription>
                    {t("zone_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <Card className="border-warning/30 bg-warning/5">
              <CardContent className="flex items-start gap-2 p-3 text-xs text-muted-foreground">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
                <span>
                  {t("warning")}
                </span>
              </CardContent>
            </Card>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/domains")}
              >
                {tParent("cancel")}
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
  );
}
