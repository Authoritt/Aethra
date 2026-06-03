"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import {
  AlertTriangle,
  Check,
  Copy,
  Eye,
  EyeOff,
  Loader2,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import {
  clearSecretFromSession,
  readSecretFromSession,
} from "../CreateKeyForm";

type LoadState =
  | { kind: "loading" }
  | { kind: "missing" }
  | { kind: "loaded"; secret: string };

export function CreatedSecretCard({ id }: { id: string | null }) {
  const t = useTranslations("pages.settings_api_keys.created");
  const router = useRouter();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [revealed, setRevealed] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!id) {
      setState({ kind: "missing" });
      return;
    }
    const secret = readSecretFromSession(id);
    if (!secret) {
      setState({ kind: "missing" });
      return;
    }
    setState({ kind: "loaded", secret });
  }, [id]);

  async function copy() {
    if (state.kind !== "loaded") return;
    try {
      await navigator.clipboard.writeText(state.secret);
      setCopied(true);
      toast.success(t("toast_copied"));
      setTimeout(() => setCopied(false), 2500);
    } catch {
      toast.error(t("toast_copy_fail"));
    }
  }

  function done() {
    if (id) clearSecretFromSession(id);
    router.push("/settings/api-keys");
    router.refresh();
  }

  if (state.kind === "loading") {
    return (
      <Card>
        <CardContent className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          {t("loading")}
        </CardContent>
      </Card>
    );
  }

  if (state.kind === "missing") {
    return (
      <Card className="border-warning/40 bg-warning/5">
        <CardContent className="flex flex-col gap-4 p-6 text-sm">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" />
            <div className="flex flex-col gap-2">
              <h2 className="text-base font-semibold text-foreground">
                {t("missing_title")}
              </h2>
              <p className="text-muted-foreground">
                {t("missing_description")}
              </p>
              <p className="text-muted-foreground">
                {t("missing_recovery")}
              </p>
            </div>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" asChild>
              <Link href="/settings/api-keys">{t("back_to_list")}</Link>
            </Button>
            <Button asChild>
              <Link href="/settings/api-keys/new">{t("create_another")}</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <Card className="border-warning/40 bg-warning/10">
        <CardContent className="flex items-start gap-3 p-4 text-sm">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" />
          <div className="flex flex-col gap-1">
            <p className="font-medium text-foreground">
              {t("warning_title")}
            </p>
            <p className="text-muted-foreground">
              {t("warning_description")}
            </p>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="flex flex-col gap-3 p-5">
          <div className="flex items-center justify-between gap-3">
            <span className="text-xs uppercase tracking-wider text-muted-foreground">
              {t("secret_label")}
            </span>
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setRevealed((v) => !v)}
              >
                {revealed ? (
                  <>
                    <EyeOff className="mr-2 h-3.5 w-3.5" />
                    {t("hide")}
                  </>
                ) : (
                  <>
                    <Eye className="mr-2 h-3.5 w-3.5" />
                    {t("show")}
                  </>
                )}
              </Button>
              <Button type="button" size="sm" onClick={copy}>
                {copied ? (
                  <>
                    <Check className="mr-2 h-3.5 w-3.5" />
                    {t("copied")}
                  </>
                ) : (
                  <>
                    <Copy className="mr-2 h-3.5 w-3.5" />
                    {t("copy")}
                  </>
                )}
              </Button>
            </div>
          </div>
          <pre
            className="overflow-x-auto rounded-md border border-border bg-muted px-4 py-3 font-mono text-sm text-foreground"
            aria-label={revealed ? t("secret_visible_aria") : t("secret_hidden_aria")}
          >
            {revealed ? state.secret : maskSecret(state.secret)}
          </pre>
          <p className="text-[11px] text-muted-foreground">
            {t("usage_prefix")}{" "}
            <code className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[10px] text-foreground">
              Authorization: Bearer{" "}
              {revealed ? state.secret.slice(0, 18) : "aethra_********"}
              ...
            </code>
          </p>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="flex items-start gap-3 p-4 text-sm">
          <Checkbox
            id="ack-secret"
            checked={acknowledged}
            onCheckedChange={(v) => setAcknowledged(v === true)}
            className="mt-0.5"
          />
          <Label
            htmlFor="ack-secret"
            className="cursor-pointer text-muted-foreground"
          >
            {t("ack_label")}
          </Label>
        </CardContent>
      </Card>

      <div className="flex justify-end">
        <Button type="button" onClick={done} disabled={!acknowledged}>
          {t("done")}
        </Button>
      </div>
    </div>
  );
}

function maskSecret(secret: string): string {
  if (secret.length <= 12) return "*".repeat(secret.length);
  const head = secret.slice(0, 10);
  return `${head}${"*".repeat(Math.max(8, secret.length - 10))}`;
}
