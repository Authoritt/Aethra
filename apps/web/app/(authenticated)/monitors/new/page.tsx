"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
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
import { Textarea } from "@/components/ui/textarea";
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type {
  CreateMonitorRequest,
  MonitorDetailDto,
  MonitorHttpMethod,
} from "@/lib/types";

const URL_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;
const SLUG_RE = /^[a-z0-9]+(-[a-z0-9]+)*$/;

export default function NewMonitorPage() {
  const t = useTranslations("pages.monitors_form");
  const tBreadcrumbs = useTranslations("breadcrumbs");
  const router = useRouter();
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [method, setMethod] = useState<MonitorHttpMethod>("GET");
  const [expected, setExpected] = useState("200");
  const [interval, setInterval] = useState(60);
  const [timeout, setTimeout] = useState(10000);
  const [headersText, setHeadersText] = useState("");
  const [body, setBody] = useState("");
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!SLUG_RE.test(slug.trim())) {
      return t("validation_slug_invalid");
    }
    if (name.trim().length === 0) return t("validation_name_required");
    if (!URL_RE.test(url.trim())) return t("validation_url_invalid");
    const codes = parseExpected(expected);
    if (codes.length === 0) return t("validation_codes_invalid");
    if (interval < 30 || interval > 3600) return t("validation_interval_range");
    if (timeout < 1000 || timeout > 60000) return t("validation_timeout_range");
    if (headersText.trim() && parseHeaders(headersText) === null)
      return t("validation_headers_invalid");
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
      const body_template = body.trim() === "" ? undefined : body;
      const headers =
        headersText.trim() === ""
          ? undefined
          : (parseHeaders(headersText) ?? undefined);
      const payload: CreateMonitorRequest = {
        slug: slug.trim(),
        name: name.trim(),
        url: url.trim(),
        http_method: method,
        expected_status_codes: parseExpected(expected),
        interval_sec: interval,
        timeout_ms: timeout,
        headers,
        body_template,
      };
      const created = await api<MonitorDetailDto>("/api/monitors/", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      toast.success(t("toast_created_simple"));
      router.push(`/monitors/${created.id}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string; Message?: string } | undefined)
              ?.detail ??
            (e.body as { Message?: string } | undefined)?.Message ??
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
          { label: tBreadcrumbs("monitors"), href: "/monitors" },
          { label: tBreadcrumbs("new") },
        ]}
        title={t("title_new")}
        description={t("description_new")}
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label htmlFor="slug">{t("label_slug")}</Label>
              <Input
                id="slug"
                value={slug}
                onChange={(e) => setSlug(e.target.value)}
                placeholder={t("placeholder_slug")}
                className="font-mono text-xs"
                required
              />
              <p className="text-xs text-muted-foreground">
                {t("slug_hint")}
              </p>
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">{t("label_name")}</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("placeholder_name")}
                required
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="url">{t("label_url")}</Label>
              <Input
                id="url"
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                placeholder={t("placeholder_url")}
                className="font-mono text-xs"
                required
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>{t("label_method")}</Label>
                <Select
                  value={method}
                  onValueChange={(v) => setMethod(v as MonitorHttpMethod)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="GET">GET</SelectItem>
                    <SelectItem value="HEAD">HEAD</SelectItem>
                    <SelectItem value="POST">POST</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label htmlFor="expected">{t("label_ok_codes")}</Label>
                <Input
                  id="expected"
                  value={expected}
                  onChange={(e) => setExpected(e.target.value)}
                  placeholder={t("placeholder_codes")}
                  className="font-mono text-xs"
                  required
                />
                <p className="text-xs text-muted-foreground">
                  {t("codes_hint")}
                </p>
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="interval">{t("label_interval_short")}</Label>
                <Input
                  id="interval"
                  type="number"
                  value={interval}
                  onChange={(e) => setInterval(Number(e.target.value) || 60)}
                  min={30}
                  max={3600}
                  step={10}
                />
                <p className="text-xs text-muted-foreground">{t("interval_hint")}</p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="timeout">{t("label_timeout")}</Label>
                <Input
                  id="timeout"
                  type="number"
                  value={timeout}
                  onChange={(e) =>
                    setTimeout(Number(e.target.value) || 10000)
                  }
                  min={1000}
                  max={60000}
                  step={500}
                />
                <p className="text-xs text-muted-foreground">{t("timeout_hint")}</p>
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="headers">{t("headers_label")}</Label>
              <Textarea
                id="headers"
                value={headersText}
                onChange={(e) => setHeadersText(e.target.value)}
                rows={3}
                placeholder={t("placeholder_headers")}
                className="font-mono text-xs"
              />
              <p className="text-xs text-muted-foreground">
                {t("headers_hint")}
              </p>
            </div>
            {method === "POST" ? (
              <div className="space-y-2">
                <Label htmlFor="body">{t("body_label")}</Label>
                <Textarea
                  id="body"
                  value={body}
                  onChange={(e) => setBody(e.target.value)}
                  rows={4}
                  placeholder={t("placeholder_body")}
                  className="font-mono text-xs"
                />
              </div>
            ) : null}

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/monitors")}
              >
                {t("cancel")}
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("submit_new")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}

function parseExpected(raw: string): number[] {
  return raw
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
    .map((s) => Number(s))
    .filter((n) => Number.isInteger(n) && n >= 100 && n <= 599);
}

function parseHeaders(raw: string): Record<string, string> | null {
  const result: Record<string, string> = {};
  for (const line of raw.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (trimmed === "") continue;
    const idx = trimmed.indexOf(":");
    if (idx <= 0) return null;
    const key = trimmed.slice(0, idx).trim();
    const value = trimmed.slice(idx + 1).trim();
    if (key === "") return null;
    result[key] = value;
  }
  return result;
}
