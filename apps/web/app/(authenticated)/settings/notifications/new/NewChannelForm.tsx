"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
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

const CHANNEL_OPTIONS: { value: ChannelTypeValue; label: string }[] = [
  { value: "Slack", label: "Slack (webhook)" },
  { value: "Discord", label: "Discord (webhook)" },
  { value: "Telegram", label: "Telegram (bot API)" },
  { value: "Email", label: "Email (SMTP)" },
  { value: "Webhook", label: "Webhook generico" },
];

export function NewChannelForm() {
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
      toast.success(`Canal "${name}" creado`);
      router.push("/settings/notifications");
      router.refresh();
    } catch (err) {
      const msg =
        err instanceof ApiError
          ? (err.body as { message?: string } | undefined)?.message ??
            `Error ${err.status}`
          : err instanceof Error
            ? err.message
            : "Error desconocido";
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
            <Label htmlFor="name">Nombre</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="alerts-team-ops"
              required
              maxLength={100}
            />
          </div>

          <div className="space-y-2">
            <Label>Tipo</Label>
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
              <Label htmlFor="webhook_url">Webhook URL</Label>
              <Input
                id="webhook_url"
                value={webhookUrl}
                onChange={(e) => setWebhookUrl(e.target.value)}
                placeholder="https://hooks.slack.com/services/..."
                required
              />
            </div>
          )}

          {type === "Telegram" && (
            <>
              <div className="space-y-2">
                <Label htmlFor="bot_token">Bot token</Label>
                <Input
                  id="bot_token"
                  value={botToken}
                  onChange={(e) => setBotToken(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="chat_id">Chat ID</Label>
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
                <Label htmlFor="smtp_cred">Credencial SMTP (nombre)</Label>
                <Input
                  id="smtp_cred"
                  value={smtpCred}
                  onChange={(e) => setSmtpCred(e.target.value)}
                  placeholder="smtp:default"
                  required
                />
                <p className="text-xs text-muted-foreground">
                  Referencia a una IntegrationCredential con shape JSON{" "}
                  <span className="font-mono">
                    {`{ host, port, username, password, useTls }`}
                  </span>
                  .
                </p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="from">From</Label>
                <Input
                  id="from"
                  value={emailFrom}
                  onChange={(e) => setEmailFrom(e.target.value)}
                  type="email"
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="to">To</Label>
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
                <Label htmlFor="wh_url">URL</Label>
                <Input
                  id="wh_url"
                  value={whUrl}
                  onChange={(e) => setWhUrl(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="wh_method">HTTP method</Label>
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
            <Label>Filtros de eventos (vacio = todos)</Label>
            <p className="mt-1 text-xs text-muted-foreground">
              Selecciona los eventos que activan este canal. Si dejas todos
              desmarcados, el canal recibe todos los eventos disponibles.
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
          Cancelar
        </Button>
        <Button type="submit" disabled={loading}>
          {loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          Crear canal
        </Button>
      </div>
    </form>
  );
}
