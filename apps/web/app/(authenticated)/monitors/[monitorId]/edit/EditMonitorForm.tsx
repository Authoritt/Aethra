"use client";

import { useRouter } from "next/navigation";
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
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api } from "@/lib/api";
import type {
  MonitorDetailDto,
  MonitorHttpMethod,
  UpdateMonitorRequest,
} from "@/lib/types";

const URL_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;

export function EditMonitorForm({ initial }: { initial: MonitorDetailDto }) {
  const router = useRouter();
  const [name, setName] = useState(initial.name);
  const [url, setUrl] = useState(initial.url);
  const [method, setMethod] = useState<MonitorHttpMethod>(initial.http_method);
  const [expected, setExpected] = useState(
    initial.expected_status_codes.join(","),
  );
  const [interval, setInterval] = useState(initial.interval_sec);
  const [timeout, setTimeout] = useState(initial.timeout_ms);
  const [headersText, setHeadersText] = useState(
    initial.headers
      ? Object.entries(initial.headers)
          .map(([k, v]) => `${k}: ${v}`)
          .join("\n")
      : "",
  );
  const [body, setBody] = useState(initial.body_template ?? "");
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!URL_RE.test(url.trim())) return "URL debe ser http(s):// absoluta.";
    const codes = parseExpected(expected);
    if (codes.length === 0)
      return "Códigos esperados inválidos: usá comas, ej. '200,204'.";
    if (interval < 30 || interval > 3600)
      return "Intervalo entre 30 y 3600 segundos.";
    if (timeout < 1000 || timeout > 60000)
      return "Timeout entre 1000 y 60000 ms.";
    if (headersText.trim() && parseHeaders(headersText) === null)
      return "Headers mal formados.";
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
      const headersParsed =
        headersText.trim() === "" ? null : parseHeaders(headersText);
      const payload: UpdateMonitorRequest = {
        name: name.trim() === initial.name ? undefined : name.trim(),
        url: url.trim() === initial.url ? undefined : url.trim(),
        http_method: method === initial.http_method ? undefined : method,
        expected_status_codes: parseExpected(expected),
        interval_sec: interval,
        timeout_ms: timeout,
        headers: headersParsed ?? undefined,
        clear_headers:
          headersText.trim() === "" && initial.headers !== null,
        body_template: body.trim() === "" ? undefined : body,
        clear_body_template:
          body.trim() === "" && initial.body_template !== null,
      };
      await api(`/api/monitors/${initial.id}`, {
        method: "PATCH",
        body: JSON.stringify(payload),
      });
      toast.success("Cambios guardados");
      router.push(`/monitors/${initial.id}`);
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
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardContent className="space-y-5 p-6">
          <div className="space-y-2">
            <Label htmlFor="name">Nombre *</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="url">URL *</Label>
            <Input
              id="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              className="font-mono text-xs"
              required
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Método</Label>
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
              <Label htmlFor="expected">Códigos OK</Label>
              <Input
                id="expected"
                value={expected}
                onChange={(e) => setExpected(e.target.value)}
                className="font-mono text-xs"
                required
              />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="interval">Intervalo (s)</Label>
              <Input
                id="interval"
                type="number"
                value={interval}
                onChange={(e) => setInterval(Number(e.target.value) || 60)}
                min={30}
                max={3600}
                step={10}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="timeout">Timeout (ms)</Label>
              <Input
                id="timeout"
                type="number"
                value={timeout}
                onChange={(e) => setTimeout(Number(e.target.value) || 10000)}
                min={1000}
                max={60000}
                step={500}
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="headers">Headers</Label>
            <Textarea
              id="headers"
              value={headersText}
              onChange={(e) => setHeadersText(e.target.value)}
              rows={3}
              className="font-mono text-xs"
            />
            <p className="text-xs text-muted-foreground">
              &apos;Clave: valor&apos; por línea. Vacío = sin headers.
            </p>
          </div>
          {method === "POST" ? (
            <div className="space-y-2">
              <Label htmlFor="body">Body</Label>
              <Textarea
                id="body"
                value={body}
                onChange={(e) => setBody(e.target.value)}
                rows={4}
                className="font-mono text-xs"
              />
            </div>
          ) : null}

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/monitors/${initial.id}`)}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Guardar cambios
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
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
