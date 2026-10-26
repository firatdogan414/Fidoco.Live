import { useTranslation } from 'react-i18next';

/** Terms of purchase and privacy policy: Fidoco is the seller (texts in i18n/locales/sv/legal.json). */
export function LegalPage({ kind }: { kind: 'terms' | 'privacy' }) {
  const { t } = useTranslation('legal');
  const paragraphs = t(`${kind}.body`, { returnObjects: true }) as string[];

  return (
    <section className="narrow">
      <h1>{t(`${kind}.title`)}</h1>
      {paragraphs.map((text) => (
        <p key={text}>{text}</p>
      ))}
      <p className="small muted">{t(`${kind}.reviewNote`)}</p>
    </section>
  );
}
