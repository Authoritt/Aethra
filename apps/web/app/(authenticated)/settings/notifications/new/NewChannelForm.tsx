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
import { Checkbox } from "@/components/ui/checkbox";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ApiError, api } from "@/lib/api";
import {
  NOTIFICATION_EVENT_TYPES,
  type NotificationChannelType,
} from "@/lib/types";

type ChannelTypeValue = NotificationChannelType;

export function NewChannelForm() {
  const t = useTranslations("pages.settings_notifications.new");
  const tParent = useTranslations("pages.settings_notifications");
  const router = useRouter();
  const [name, setName] = useState("");
  const [type, setType] = useState<ChannelTypeValue>("Slack");
  const [filters, setFilters] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);

  // Campos por tipo.
  const [webhookUrl, setWebhookUrl] = useState("");
  const [botToken, setBotToken] = useState("");
  const [chatId, setChatId] = useState("");
  const [smtpCred, setSmtpCred] = useState("smtp:default");
  const [emailFrom, setEmailFrom] = useState("");
  const [emailTo, setEmailTo] = useState("");
  const [whUrl, setWhUrl] = useState("");
  const [whMethod, setWhMethod] = useState("POST");

  const CHANNEL_OPTIONS: { value: ChannelTypeValue; label: string }[] = [
    { value: "Slack", label: t("channel_slack") },
    { value: "Discord", label: t("channel_discord") },
    { value: "Telegram", label: t("channel_telegram") },
    { value: "Email", label: t("channel_email") },
    { value: "Webhook", label: t("channel_webhook") },
  ];

  function toggleFilter(ev: string) {
    setFilters((prev) =>
      prev.includes(ev) ? prev.filter((x) => x !== ev) : [...prev, ev],
    );
  }

  function buildConfig(): Record<string, unknown> {
    switch (type) {
      case "Slack":
        return { webhook_url: webhookUrl };
      case "Discord":
        return { webhook_url: webhookUrl };
      case "Telegram":
        return { bot_token: botToken, chat_id: chatId };
      case "Email":
        return {
          smtp_credential_name: smtpCred,
          from: emailFrom,
          to: emailTo,
        };
      case "Webhook":
        return { url: whUrl, http_method: whMethod };
    }
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      const body = {
        name: name.trim(),
        type,
        config: buildConfig(),
        eventFilters: filters,
      };
      await api(`/api/notifications/channels/`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_created", { name }));
      router.push("/settings/notifications");
      router.refresh();
    } catch (err) {
      const msg =
        err instanceof ApiError
          ? (err.body as { message?: string } | undefined)?.message ??
            `Error ${err.status}`
          : err instanceof Error
            ? err.message
            : tParent("error_unknown");
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="mt-6 space-y-6">
      <Card>
        <CardContent className="space-y-4 p-5">
          <div className="space-y-2">
            <Label htmlFor="name">{t("label_name")}</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("placeholder_name")}
              required
              maxLength={100}
            />
          </div>

          <div className="space-y-2">
            <Label>{t("label_type")}</Label>
            <Select
              value={type}
              onValueChange={(v) => setType(v as ChannelTypeValue)}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CHANNEL_OPTIONS.map((o) => (
                  <SelectItem key={o.value} value={o.value}>
                    {o.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {(type === "Slack" || type === "Discord") && (
            <div className="space-y-2">
              <Label htmlFor="webhook_url">{t("label_webhook_url")}</Label>
              <Input
                id="webhook_url"
                value={webhookUrl}
                onChange={(e) => setWebhookUrl(e.target.value)}
                placeholder={t("placeholder_webhook_url")}
                required
              />
            </div>
          )}

          {type === "Telegram" && (
            <>
              <div className="space-y-2">
                <Label htmlFor="bot_token">{t("label_bot_token")}</Label>
                <Input
                  id="bot_token"
                  value={botToken}
                  onChange={(e) => setBotToken(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="chat_id">{t("label_chat_id")}</Label>
                <Input
                  id="chat_id"
                  value={chatId}
                  onChange={(e) => setChatId(e.target.value)}
                  required
                />
              </div>
            </>
          )}

          {type === "Email" && (
            <>
              <div className="space-y-2">
                <Label htmlFor="smtp_cred">{t("label_smtp_cred")}</Label>
                <Input
                  id="smtp_cred"
                  value={smtpCred}
                  onChange={(e) => setSmtpCred(e.target.value)}
                  placeholder={t("placeholder_smtp_cred")}
                  required
                />
                <p className="text-xs text-muted-foreground">
                  {t("smtp_cred_hint_prefix")}{" "}
                  <span className="font-mono">
                    {`{ host, port, username, password, useTls }`}
                  </span>
                  .
                </p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="from">{t("label_from")}</Label>
                <Input
                  id="from"
                  value={emailFrom}
                  onChange={(e) => setEmailFrom(e.target.value)}
                  type="email"
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="to">{t("label_to")}</Label>
                <Input
                  id="to"
                  value={emailTo}
                  onChange={(e) => setEmailTo(e.target.value)}
                  type="email"
                  required
                />
              </div>
            </>
          )}

          {type === "Webhook" && (
            <>
              <div className="space-y-2">
                <Label htmlFor="wh_url">{t("label_url")}</Label>
                <Input
                  id="wh_url"
                  value={whUrl}
                  onChange={(e) => setWhUrl(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="wh_method">{t("label_http_method")}</Label>
                <Select value={whMethod} onValueChange={setWhMethod}>
                  <SelectTrigger id="wh_method">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {["POST", "PUT", "PATCH"].map((m) => (
                      <SelectItem key={m} value={m}>
                        {m}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 p-5">
          <div>
            <Label>{t("label_filters")}</Label>
            <p className="mt-1 text-xs text-muted-foreground">
              {t("filters_hint")}
            </p>
          </div>
          <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
            {NOTIFICATION_EVENT_TYPES.map((ev) => (
              <label
                key={ev}
                className="flex items-center gap-2 rounded-md border border-border bg-muted/20 p-2"
              >
                <Checkbox
                  checked={filters.includes(ev)}
                  onCheckedChange={() => toggleFilter(ev)}
                />
                <span className="font-mono text-xs">{ev}</span>
              </label>
            ))}
          </div>
        </CardContent>
      </Card>

      <div className="flex items-center justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          onClick={() => router.push("/settings/notifications")}
        >
          {tParent("cancel")}
        </Button>
        <Button type="submit" disabled={loading}>
          {loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          {tParent("submit")}
        </Button>
      </div>
    </form>
  );
}
