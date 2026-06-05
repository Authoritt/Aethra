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
import { Switch } from "@/components/ui/switch";
import { ApiError, api } from "@/lib/api";
import type { DnsRecordDto, DnsRecordType } from "@/lib/types";

const TYPES: DnsRecordType[] = ["A", "AAAA", "CNAME", "TXT", "MX"];
const FQDN_RE =
  /^(?=.{1,253}$)(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;

/** Edita un DNS record (PATCH /api/cloudflare/records/{id}). */
export function EditDnsRecordForm({
  zoneId,
  record,
}: {
  zoneId: string;
  record: DnsRecordDto;
}) {
  const t = useTranslations("pages.cloudflare_record_new");
  const router = useRouter();

  const [type, setType] = useState<DnsRecordType>(record.type);
  const [name, setName] = useState(record.name);
  const [content, setContent] = useState(record.content);
  const [ttl, setTtl] = useState<number>(record.ttl);
  const [proxied, setProxied] = useState(record.proxied);
  const [comment, setComment] = useState(record.comment ?? "");
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(name.trim())) return t("validation_name_fqdn");
    if (!content.trim()) return t("validation_content_required");
    if (ttl < 1 || ttl > 86400) return t("validation_ttl_range");
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
      await api<DnsRecordDto>(`/api/cloudflare/records/${record.id}`, {
        method: "PATCH",
        body: JSON.stringify({
          type,
          name: name.trim().toLowerCase(),
          content: content.trim(),
          ttl,
          proxied,
          comment: comment.trim() || null,
        }),
      });
      toast.success("Record actualizado");
      router.push(`/cloudflare/${zoneId}`);
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
            : t("error_unknown");
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  function contentHint(rt: DnsRecordType): string {
    switch (rt) {
      case "A":
        return t("content_hint_a");
      case "AAAA":
        return t("content_hint_aaaa");
      case "CNAME":
        return t("content_hint_cname");
      case "MX":
        return t("content_hint_mx");
      case "TXT":
        return t("content_hint_txt");
    }
  }

  return (
    <Card className="max-w-2xl">
      <CardContent className="p-6">
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <div className="space-y-2">
            <Label>{t("label_type")}</Label>
            <Select
              value={type}
              onValueChange={(v) => setType(v as DnsRecordType)}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TYPES.map((rt) => (
                  <SelectItem key={rt} value={rt}>
                    {rt}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="name">{t("label_name")}</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("placeholder_name")}
              className="font-mono text-xs"
              autoComplete="off"
              spellCheck={false}
              required
            />
            <p className="text-xs text-muted-foreground">
              {t("name_hint")}
            </p>
          </div>
          <div className="space-y-2">
            <Label htmlFor="content">{t("label_content")}</Label>
            <Input
              id="content"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder={contentPlaceholder(type)}
              className="font-mono text-xs"
              autoComplete="off"
              spellCheck={false}
              required
            />
            <p className="text-xs text-muted-foreground">
              {contentHint(type)}
            </p>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="ttl">{t("label_ttl_short")}</Label>
              <Input
                id="ttl"
                type="number"
                value={ttl}
                min={1}
                max={86400}
                onChange={(e) => setTtl(Number(e.target.value))}
                required
              />
              <p className="text-xs text-muted-foreground">
                {t("ttl_hint")}
              </p>
            </div>
            <div className="space-y-2">
              <Label>{t("proxied_label")}</Label>
              <div className="flex items-center gap-3 rounded-md border border-input bg-background px-3 py-2">
                <Switch
                  id="proxied"
                  checked={proxied}
                  onCheckedChange={setProxied}
                />
                <Label
                  htmlFor="proxied"
                  className="cursor-pointer text-xs text-muted-foreground"
                >
                  {t("proxied_switch_hint")}
                </Label>
              </div>
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="comment">{t("comment_label")}</Label>
            <Input
              id="comment"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder={t("comment_placeholder")}
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              {t("comment_hint")}
            </p>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/cloudflare/${zoneId}`)}
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
  );
}

function contentPlaceholder(type: DnsRecordType): string {
  switch (type) {
    case "A":
      return "203.0.113.10";
    case "AAAA":
      return "2001:db8::1";
    case "CNAME":
      return "target.example.com";
    case "MX":
      return "mail.example.com";
    case "TXT":
      return "v=spf1 include:_spf.example.com ~all";
  }
}
