"use client";

import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
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
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type {
  CreateDnsRecordRequest,
  DnsRecordDto,
  DnsRecordType,
} from "@/lib/types";

const TYPES: DnsRecordType[] = ["A", "AAAA", "CNAME", "TXT", "MX"];
const FQDN_RE =
  /^(?=.{1,253}$)(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;

export default function NewDnsRecordPage() {
  const router = useRouter();
  const params = useParams<{ zoneId: string }>();
  const zoneId = params.zoneId;

  const [type, setType] = useState<DnsRecordType>("A");
  const [name, setName] = useState("");
  const [content, setContent] = useState("");
  const [ttl, setTtl] = useState<number>(300);
  const [proxied, setProxied] = useState(false);
  const [comment, setComment] = useState("");
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(name.trim()))
      return "El nombre debe ser un FQDN válido (ej. api.example.com).";
    if (!content.trim()) return "El contenido es obligatorio.";
    if (ttl < 1 || ttl > 86400) return "TTL debe estar entre 1 y 86400.";
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
      const body: CreateDnsRecordRequest = {
        type,
        name: name.trim().toLowerCase(),
        content: content.trim(),
        ttl,
        proxied,
        comment: comment.trim() || undefined,
      };
      await api<DnsRecordDto>(`/api/cloudflare/zones/${zoneId}/records`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Record creado");
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
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Cloudflare", href: "/cloudflare" },
          { label: "Zona", href: `/cloudflare/${zoneId}` },
          { label: "Nuevo record" },
        ]}
        title="Nuevo DNS record"
        description="Se crea en Cloudflare y se guarda localmente con el id devuelto por la API."
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label>Tipo *</Label>
              <Select
                value={type}
                onValueChange={(v) => setType(v as DnsRecordType)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {TYPES.map((t) => (
                    <SelectItem key={t} value={t}>
                      {t}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">Nombre *</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="api.example.com"
                className="font-mono text-xs"
                autoComplete="off"
                spellCheck={false}
                required
              />
              <p className="text-xs text-muted-foreground">
                FQDN sin acortar.
              </p>
            </div>
            <div className="space-y-2">
              <Label htmlFor="content">Contenido *</Label>
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
                <Label htmlFor="ttl">TTL *</Label>
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
                  Segundos. 1 = auto en Cloudflare.
                </p>
              </div>
              <div className="space-y-2">
                <Label>Proxied</Label>
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
                    Tráfico vía proxy Cloudflare
                  </Label>
                </div>
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="comment">Comentario</Label>
              <Input
                id="comment"
                value={comment}
                onChange={(e) => setComment(e.target.value)}
                placeholder="gestionado por Aethra"
                spellCheck={false}
              />
              <p className="text-xs text-muted-foreground">
                Opcional. Aparece en el panel de Cloudflare.
              </p>
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push(`/cloudflare/${zoneId}`)}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear record
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}

function contentHint(type: DnsRecordType): string {
  switch (type) {
    case "A":
      return "IPv4 de destino (ej. 203.0.113.10).";
    case "AAAA":
      return "IPv6 de destino.";
    case "CNAME":
      return "FQDN de destino al que apunta el alias.";
    case "MX":
      return "Servidor de correo, con prioridad si Cloudflare lo requiere.";
    case "TXT":
      return "Texto libre. SPF/DKIM/etc.";
  }
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
