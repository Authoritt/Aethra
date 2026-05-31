import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Aethra",
  description: "Plataforma unificada de despliegue, monitoreo y operación.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="es" className="h-full antialiased">
      <body className="min-h-full flex flex-col bg-zinc-950 font-sans">
        {children}
      </body>
    </html>
  );
}
