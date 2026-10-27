import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi } from '../../api/admin';
import { useTenant } from '../../app/tenant';
import { ErrorMessage, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { ImageUpload } from './ImageUpload';

/** The organizer's own branding: logo, colours and support address. */
export function AdminProfilePage() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const tenant = useTenant();
  const queryClient = useQueryClient();
  const [logoUrl, setLogoUrl] = useState(tenant.logoUrl);
  const [primaryColor, setPrimaryColor] = useState(tenant.primaryColor);
  const [accentColor, setAccentColor] = useState(tenant.accentColor);
  const [supportEmail, setSupportEmail] = useState(tenant.supportEmail ?? '');
  const save = useMutation({
    mutationFn: () => adminApi.updateBranding({ logoUrl, primaryColor, accentColor, supportEmail: supportEmail || null }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['tenant'] }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <section className="narrow">
      <h1>{t('profile.title')}</h1>
      <form className="form panel" onSubmit={submit}>
        <span>{t('profile.logo')}</span>
        {logoUrl ? (
          <div className="logo-preview" style={{ background: primaryColor }}>
            <img src={logoUrl} alt="" />
          </div>
        ) : null}
        <div className="copy-row">
          <ImageUpload onUploaded={setLogoUrl} />
          {logoUrl ? (
            <button type="button" className="link-button small" onClick={() => setLogoUrl(null)}>
              {t('profile.removeLogo')}
            </button>
          ) : null}
        </div>
        <div className="copy-row">
          <label>
            {t('profile.primaryColor')}
            <input type="color" value={primaryColor} onChange={(e) => setPrimaryColor(e.target.value)} />
          </label>
          <label>
            {t('profile.accentColor')}
            <input type="color" value={accentColor} onChange={(e) => setAccentColor(e.target.value)} />
          </label>
        </div>
        <label>
          {t('profile.supportEmail')}
          <input type="email" value={supportEmail} onChange={(e) => setSupportEmail(e.target.value)} />
        </label>
        <button type="submit" className="button button-primary" disabled={save.isPending}>
          {t('profile.save')}
        </button>
        {save.isSuccess ? <Notice>{t('profile.saved')}</Notice> : null}
        {save.isError ? <ErrorMessage>{errorText(save.error)}</ErrorMessage> : null}
      </form>
      <EraseCustomer />
    </section>
  );
}

/** GDPR requests from buyers handled by the organizer. */
function EraseCustomer() {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const [email, setEmail] = useState('');
  const erase = useMutation({ mutationFn: () => adminApi.eraseCustomer(email), onSuccess: () => setEmail('') });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (window.confirm(t('gdpr.confirm'))) erase.mutate();
  };

  return (
    <form className="form panel" onSubmit={submit}>
      <h2>{t('gdpr.title')}</h2>
      <p className="small muted">{t('gdpr.help')}</p>
      <label>
        {t('gdpr.email')}
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <button type="submit" className="button button-danger" disabled={erase.isPending}>
        {t('gdpr.erase')}
      </button>
      {erase.isSuccess ? <Notice>{t('gdpr.done')}</Notice> : null}
      {erase.isError ? <ErrorMessage>{errorText(erase.error)}</ErrorMessage> : null}
    </form>
  );
}
