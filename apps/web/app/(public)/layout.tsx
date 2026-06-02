import type { ReactNode } from "react";

/**
 * Layout para páginas públicas (login, recuperación de contraseña, etc).
 * Centra el contenido en un viewport con padding generoso — el caller
 * (page) define su propia card/form.
 */
export default function PublicLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4 py-12">
      {children}
    </div>
  );
}
