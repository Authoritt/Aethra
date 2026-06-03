import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { NewChannelForm } from "./NewChannelForm";

export default async function NewNotificationChannelPage() {
  const t = await getTranslations("pages.settings_notifications");
  const tBreadcrumbs = await getTranslations("breadcrumbs");
  return (
    <div className="mx-auto max-w-3xl px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("settings"), href: "/settings" },
          { label: tBreadcrumbs("notifications"), href: "/settings/notifications" },
          { label: t("breadcrumb") },
        ]}
        title={t("title")}
        description={t("description")}
      />
      <NewChannelForm />
    </div>
  );
}
