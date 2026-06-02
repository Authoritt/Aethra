import { PageHeader } from "@/components/layout/page-header";
import { NewChannelForm } from "./NewChannelForm";

export default function NewNotificationChannelPage() {
  return (
    <div className="mx-auto max-w-3xl px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Notificaciones", href: "/settings/notifications" },
          { label: "Nuevo" },
        ]}
        title="Crear canal de notificacion"
        description="Configura un webhook o cuenta de email para recibir alertas operativas. La config se cifra con DataProtection antes de persistirse."
      />
      <NewChannelForm />
    </div>
  );
}
