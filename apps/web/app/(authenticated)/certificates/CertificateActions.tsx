"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, RefreshCw, ShieldPlus } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { CertificateDto } from "@/lib/types";

export function RequestCertificateForm() {
  const router = useRouter();
  const [hostname, setHostname] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit() {
    const value = hostname.trim();
    if (!value) return;
    setBusy(true);
    try {
      await api<CertificateDto>("/api/proxy/certificates/request", {
        method: "POST",
        body: JSON.stringify({ hostname: value }),
      });
      toast.success("Solicitud de certificado iniciada");
      setHostname("");
      router.refresh();
    } catch (e) {
      toast.error(readApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-2 sm:flex-row">
      <Input
        value={hostname}
        onChange={(event) => setHostname(event.target.value)}
        placeholder="app.example.com"
        className="font-mono text-xs"
      />
      <Button type="button" onClick={submit} disabled={busy || hostname.trim().length === 0}>
        {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <ShieldPlus className="mr-2 h-4 w-4" />}
        Emitir
      </Button>
    </div>
  );
}

export function RenewCertificateButton({ certificateId }: { certificateId: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function renew() {
    setBusy(true);
    try {
      await api<CertificateDto>(`/api/proxy/certificates/${encodeURIComponent(certificateId)}/renew`, {
        method: "POST",
      });
      toast.success("Renovacion iniciada");
      router.refresh();
    } catch (e) {
      toast.error(readApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" size="sm" variant="outline" onClick={renew} disabled={busy}>
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
      Renovar
    </Button>
  );
}

function readApiError(e: unknown) {
  return e instanceof ApiError
    ? (e.body as { message?: string; detail?: string; Message?: string } | undefined)
        ?.message ??
      (e.body as { detail?: string } | undefined)?.detail ??
      `Error ${e.status}`
    : e instanceof Error
      ? e.message
      : "Error desconocido";
}
