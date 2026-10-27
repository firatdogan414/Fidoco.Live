import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import admin from './locales/sv/admin.json';
import common from './locales/sv/common.json';
import legal from './locales/sv/legal.json';
import platform from './locales/sv/platform.json';
import viewer from './locales/sv/viewer.json';

// Swedish is the only language in phase 1. Add a folder per language under ./locales and register
// it here; components only use translation keys.
export const resources = {
  sv: { common, viewer, legal, admin, platform },
} as const;

void i18n.use(initReactI18next).init({
  resources,
  lng: 'sv',
  fallbackLng: 'sv',
  defaultNS: 'common',
  ns: ['common', 'viewer', 'legal', 'admin', 'platform'],
  interpolation: { escapeValue: false },
});

export default i18n;
