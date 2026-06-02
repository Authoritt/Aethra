import { Skeleton } from "@/components/ui/skeleton";

/**
 * Loading UI mostrada por Next 16 cuando una server-route del shell autenticado
 * está fetcheando (Suspense boundary). Mantiene el chrome estable y evita la
 * sensación de "se colgó".
 */
export default function AuthenticatedLoading() {
  return (
    <div className="px-6 py-8 md:px-10 md:py-10 space-y-8 animate-fade-in">
      <div className="space-y-3">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-4 w-80 max-w-full" />
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>

      <div className="space-y-3">
        <Skeleton className="h-5 w-32" />
        <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-32 rounded-lg" />
          ))}
        </div>
      </div>
    </div>
  );
}
