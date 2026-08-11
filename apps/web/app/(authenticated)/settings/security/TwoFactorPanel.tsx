"use client";

import { useState } from "react";
import Image from "next/image";
import { Loader2, ShieldCheck, ShieldX } from "lucide-react";
import QRCode from "qrcode";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, api } from "@/lib/api";

interface EnrollResponse {
  otpauth_uri: string;
  secret_b32: string;
  issuer: string;
  account: string;
}

interface VerifyResponse {
  enabled: boolean;
  recovery_codes: string[];
}

interface RegenerateResponse {
  recovery_codes: string[];
}

type Step = "idle" | "enrolling" | "showing-qr" | "verifying" | "done";

export function TwoFactorPanel({
  email,
  initiallyEnabled,
  recoveryCodesRemaining,
}: {
  email: string;
  initiallyEnabled: boolean;
  recoveryCodesRemaining: number | null;
}) {
  const [enabled, setEnabled] = useState(initiallyEnabled);
  const [step, setStep] = useState<Step>("idle");
  const [enroll, setEnroll] = useState<EnrollResponse | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [showDisable, setShowDisable] = useState(false);
  const [disableCode, setDisableCode] = useState("");
  const [regenCode, setRegenCode] = useState("");

  async function startEnroll() {
    setBusy(true);
    setStep("enrolling");
    setQrDataUrl(null);
    try {
      const data = await api<EnrollResponse>("/api/identity/me/totp/enroll", {
        method: "POST",
      });
      const dataUrl = await QRCode.toDataURL(data.otpauth_uri, {
        width: 240,
        margin: 1,
        errorCorrectionLevel: "M",
        color: {
          dark: "#020617",
          light: "#ffffff",
        },
      });
      setEnroll(data);
      setQrDataUrl(dataUrl);
      setStep("showing-qr");
    } catch (e) {
      toast.error(formatError(e, "Failed to start enrollment"));
      setStep("idle");
    } finally {
      setBusy(false);
    }
  }

  async function verifyCode() {
    if (!code.trim()) {
      toast.error("Enter the 6-digit code from your authenticator app");
      return;
    }
    setBusy(true);
    try {
      const data = await api<VerifyResponse>("/api/identity/me/totp/verify", {
        method: "POST",
        body: JSON.stringify({ code: code.trim() }),
      });
      setEnabled(data.enabled);
      setRecoveryCodes(data.recovery_codes);
      setStep("done");
      toast.success("Two-factor authentication enabled");
    } catch (e) {
      toast.error(formatError(e, "Invalid code"));
    } finally {
      setBusy(false);
    }
  }

  async function disable2fa() {
    if (!disableCode.trim()) {
      toast.error("Enter a code to confirm");
      return;
    }
    setBusy(true);
    try {
      await api("/api/identity/me/totp/disable", {
        method: "POST",
        body: JSON.stringify({ code: disableCode.trim() }),
      });
      setEnabled(false);
      setShowDisable(false);
      setDisableCode("");
      setQrDataUrl(null);
      setRecoveryCodes(null);
      setStep("idle");
      toast.success("Two-factor authentication disabled");
    } catch (e) {
      toast.error(formatError(e, "Failed to disable 2FA"));
    } finally {
      setBusy(false);
    }
  }

  async function regenerate() {
    if (!regenCode.trim()) {
      toast.error("Enter a code to confirm");
      return;
    }
    setBusy(true);
    try {
      const data = await api<RegenerateResponse>(
        "/api/identity/me/totp/regenerate-recovery-codes",
        {
          method: "POST",
          body: JSON.stringify({ code: regenCode.trim() }),
        },
      );
      setRecoveryCodes(data.recovery_codes);
      setRegenCode("");
      toast.success("Recovery codes regenerated");
    } catch (e) {
      toast.error(formatError(e, "Failed to regenerate"));
    } finally {
      setBusy(false);
    }
  }

  if (enabled && step !== "done") {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <Badge variant="success" className="gap-1">
            <ShieldCheck className="h-3 w-3" />
            Enabled
          </Badge>
          <span className="text-sm text-muted-foreground">
            Your account is protected with TOTP 2FA ({email}).
          </span>
        </div>
        {recoveryCodesRemaining != null ? (
          <p className="text-sm text-muted-foreground">
            Recovery codes remaining: <strong>{recoveryCodesRemaining}/10</strong>
          </p>
        ) : null}

        <div className="rounded-md border border-border p-4 space-y-3">
          <div className="text-sm font-medium">Regenerate recovery codes</div>
          <p className="text-xs text-muted-foreground">
            Invalidates the current set and produces 10 new ones.
          </p>
          <div className="flex gap-2 items-end">
            <div className="flex-1">
              <Label htmlFor="regen-code" className="text-xs">
                Current TOTP code or unused recovery code
              </Label>
              <Input
                id="regen-code"
                value={regenCode}
                onChange={(e) => setRegenCode(e.target.value)}
                placeholder="123456"
                className="font-mono"
              />
            </div>
            <Button onClick={regenerate} disabled={busy || !regenCode}>
              {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Regenerate
            </Button>
          </div>
        </div>

        {recoveryCodes ? (
          <RecoveryCodesPanel codes={recoveryCodes} />
        ) : null}

        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-4 space-y-3">
          <div className="text-sm font-medium text-destructive">Disable 2FA</div>
          {showDisable ? (
            <div className="flex gap-2 items-end">
              <div className="flex-1">
                <Label htmlFor="disable-code" className="text-xs">
                  Current TOTP code or unused recovery code
                </Label>
                <Input
                  id="disable-code"
                  value={disableCode}
                  onChange={(e) => setDisableCode(e.target.value)}
                  placeholder="123456"
                  className="font-mono"
                />
              </div>
              <Button
                variant="destructive"
                onClick={disable2fa}
                disabled={busy || !disableCode}
              >
                {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
                Confirm disable
              </Button>
              <Button
                variant="outline"
                onClick={() => setShowDisable(false)}
                disabled={busy}
              >
                Cancel
              </Button>
            </div>
          ) : (
            <Button variant="destructive" onClick={() => setShowDisable(true)}>
              <ShieldX className="mr-2 h-4 w-4" />
              Disable
            </Button>
          )}
        </div>
      </div>
    );
  }

  if (step === "done" && recoveryCodes) {
    return (
      <div className="space-y-4">
        <Badge variant="success" className="gap-1">
          <ShieldCheck className="h-3 w-3" />
          2FA enabled
        </Badge>
        <p className="text-sm text-muted-foreground">
          Save these recovery codes in a safe place. <strong>They are shown only once.</strong>
        </p>
        <RecoveryCodesPanel codes={recoveryCodes} />
      </div>
    );
  }

  if (step === "showing-qr" && enroll && qrDataUrl) {
    return (
      <div className="space-y-4">
        <div>
          <p className="text-sm">
            Scan the QR with your authenticator app (Google Authenticator, 1Password, Authy):
          </p>
        </div>
        <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-start">
          <Image
            src={qrDataUrl}
            alt="2FA QR code"
            width={240}
            height={240}
            unoptimized
            className="rounded-md border bg-white p-2"
          />
          <div className="space-y-2 text-sm">
            <div>
              <Label className="text-xs">Issuer</Label>
              <div className="font-mono text-xs">{enroll.issuer}</div>
            </div>
            <div>
              <Label className="text-xs">Account</Label>
              <div className="font-mono text-xs">{enroll.account}</div>
            </div>
            <div>
              <Label className="text-xs">Secret (if manual entry)</Label>
              <code className="block break-all rounded bg-muted p-2 text-xs">
                {enroll.secret_b32}
              </code>
            </div>
          </div>
        </div>
        <div className="space-y-2">
          <Label htmlFor="verify-code">Enter the 6-digit code from your app to confirm</Label>
          <div className="flex gap-2">
            <Input
              id="verify-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="123456"
              className="font-mono w-40"
              maxLength={6}
            />
            <Button onClick={verifyCode} disabled={busy || code.length !== 6}>
              {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Verify and enable
            </Button>
            <Button
              variant="outline"
              onClick={() => {
                setStep("idle");
                setEnroll(null);
                setQrDataUrl(null);
                setCode("");
              }}
              disabled={busy}
            >
              Cancel
            </Button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3">
        <Badge variant="outline">Disabled</Badge>
        <span className="text-sm text-muted-foreground">
          Add a second factor to protect your account.
        </span>
      </div>
      <Button onClick={startEnroll} disabled={busy}>
        {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
        <ShieldCheck className="mr-2 h-4 w-4" />
        Enable 2FA
      </Button>
    </div>
  );
}

function RecoveryCodesPanel({ codes }: { codes: string[] }) {
  function downloadAsText() {
    const blob = new Blob([codes.join("\n")], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "aethra-recovery-codes.txt";
    a.click();
    URL.revokeObjectURL(url);
  }
  return (
    <div className="rounded-md border border-border bg-muted/30 p-4">
      <div className="mb-3 flex items-center justify-between">
        <div className="text-sm font-medium">Recovery codes</div>
        <Button size="sm" variant="outline" onClick={downloadAsText}>
          Download
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
        {codes.map((c, i) => (
          <code
            key={i}
            className="rounded bg-background px-2 py-1 text-center font-mono text-sm"
          >
            {c}
          </code>
        ))}
      </div>
    </div>
  );
}

function formatError(e: unknown, fallback: string): string {
  if (e instanceof ApiError) {
    return (
      (e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`
    );
  }
  if (e instanceof Error) return e.message;
  return fallback;
}
