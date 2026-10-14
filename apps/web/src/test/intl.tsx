import { render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import type { ReactElement, ReactNode } from "react";
import ar from "../messages/ar/common.json";
import arCms from "../messages/ar/cms.json";
import arErrors from "../messages/ar/errors.json";
import en from "../messages/en/common.json";
import enCms from "../messages/en/cms.json";
import enErrors from "../messages/en/errors.json";

const catalogues = {
  en: { common: en, cms: enCms, errors: enErrors },
  ar: { common: ar, cms: arCms, errors: arErrors },
};

export function Intl({ locale = "en", children }: { locale?: "en" | "ar"; children: ReactNode }) {
  return (
    <NextIntlClientProvider locale={locale} messages={catalogues[locale]} timeZone="Asia/Amman">
      {children}
    </NextIntlClientProvider>
  );
}

export const renderIntl = (ui: ReactElement, locale: "en" | "ar" = "en") => render(ui, { wrapper: ({ children }) => <Intl locale={locale}>{children}</Intl> });
