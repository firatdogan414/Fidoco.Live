import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import common from './locales/sv/common.json';

// Swedish is the only language in phase 1. Add a folder per language under ./locales
// and register it here; components only use translation keys.
export const resources = {
  sv: { common },
} as const;

void i18n.use(initReactI18next).init({
  resources,
  lng: 'sv',
  fallbackLng: 'sv',
  defaultNS: 'common',
  interpolation: { escapeValue: false },
});

export default i18n;
