"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { KeyRound, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, api } from "@/lib/api";
import type { RotateCloudflareTokenRequest } from "@/lib/types";

export function RotateTokenButton({ zoneId }: { zoneId: string }) {
  const t = useTranslations("pages.cloudflare_detail.rotate_token");
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [token, setToken] = useState("");
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (token.trim().length < 8) {
      toast.error(t("validation_short"));
      return;
    }
    setLoading(true);
    try {
      const body: RotateCloudflareTokenRequest = { apiToken: token.trim() };
      await api(`/api/cloudflare/zones/${zoneId}/rotate-token`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(t("toast_success"));
      setToken("");
      setOpen(false);
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

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
        <KeyRound className="mr-2 h-4 w-4" />
        {t("button_label")}
      </Button>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("dialog_title")}</DialogTitle>
          <DialogDescription>{t("dialog_description")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="flex flex-col gap-3">
          <div className="space-y-2">
            <Label htmlFor="token">{t("label_new_token")}</Label>
            <Input
              id="token"
              type="password"
              value={token}
              onChange={(e) => setToken(e.target.value)}
              placeholder={t("placeholder_token")}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)} type="button">
              {t("cancel")}
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {t("submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
