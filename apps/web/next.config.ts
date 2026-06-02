import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Plugin de next-intl: le decimos qué archivo expone `getRequestConfig`.
// Sin esto, next-intl busca por defecto `./i18n/request.ts`.
const withNextIntl = createNextIntlPlugin("./i18n.ts");

const nextConfig: NextConfig = {
  /* config options here */
};

export default withNextIntl(nextConfig);
