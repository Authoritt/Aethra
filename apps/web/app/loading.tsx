import { Loader2 } from "lucide-react";

/**
 * Loading UI a nivel root — cubre las routes que no estén bajo un route group
 * con su propio `loading.tsx`. Es minimal porque el chrome de Aethra vive en
 * los grupos `(authenticated)` y `(public)`.
 */
export default function RootLoading() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background">
      <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
    </div>
  );
}
