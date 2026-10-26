import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { platformApi, type PlatformTenant } from '../../api/platform';
import { ErrorMessage, Loading, Notice } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { parseKronor } from '../../shared/format';

export function TenantPage() {
  const { id = '' } = useParams();
  const errorText = useErrorText();
  const tenant = useQuery({ queryKey: ['platform-tenant', id], queryFn: () => platformApi.tenant(id) });

  if (tenant.isPending) return <Loading />;
  if (tenant.isError) return <ErrorMessage>{errorText(tenant.error)}</ErrorMessage>;

  return (
    <section className="narrow">
      <h1>{tenant.data.name}</h1>
      <BrandingForm key={`b-${tenant.data.id}`} tenant={tenant.data} />
      <TermsForm key={`t-${tenant.data.id}`} tenant={tenant.data} />
      <Domains tenant={tenant.data} />
      <InviteAdmin tenantId={tenant.data.id} />
    </section>
  );
}

function useTenantUpdate() {
  const queryClient = useQueryClient();
  return (tenant: PlatformTenant) => {
    queryClient.setQueryData(['platform-tenant', tenant.id], tenant);
    void queryClient.invalidateQueries({ queryKey: ['platform-tenants'] });
  };
}

function BrandingForm({ tenant }: { tenant: PlatformTenant }) {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const onSaved = useTenantUpdate();
  const [name, setName] = useState(tenant.name);
  const [supportEmail, setSupportEmail] = useState(tenant.supportEmail ?? '');
  const [logoUrl, setLogoUrl] = useState(tenant.logoUrl ?? '');
  const [primaryColor, setPrimaryColor] = useState(tenant.primaryColor);
  const [accentColor, setAccentColor] = useState(tenant.accentColor);
  const [suspended, setSuspended] = useState(tenant.status === 'suspended');
  const save = useMutation({
    mutationFn: () =>
      platformApi.updateTenant(tenant.id, { name, supportEmail: supportEmail || null, logoUrl: logoUrl || null, primaryColor, accentColor, suspended }),
    onSuccess: onSaved,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form className="form panel" onSubmit={submit}>
      <h2>{t('tenant.branding')}</h2>
      <label>
        {t('tenants.name')}
        <input required value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label>
        {t('tenants.supportEmail')}
        <input type="email" value={supportEmail} onChange={(e) => setSupportEmail(e.target.value)} />
      </label>
      <label>
        {t('tenant.logoUrl')}
        <input value={logoUrl} onChange={(e) => setLogoUrl(e.target.value)} />
      </label>
      <div className="copy-row">
        <label>
          {t('tenant.primaryColor')}
          <input type="color" value={primaryColor} onChange={(e) => setPrimaryColor(e.target.value)} />
        </label>
        <label>
          {t('tenant.accentColor')}
          <input type="color" value={accentColor} onChange={(e) => setAccentColor(e.target.value)} />
        </label>
      </div>
      <label className="checkbox">
        <input type="checkbox" checked={suspended} onChange={(e) => setSuspended(e.target.checked)} /> {t('tenant.suspend')}
      </label>
      <button type="submit" className="button button-primary" disabled={save.isPending}>
        {t('tenant.saveBranding')}
      </button>
      {save.isSuccess ? <Notice>{t('tenant.saved')}</Notice> : null}
      {save.isError ? <ErrorMessage>{errorText(save.error)}</ErrorMessage> : null}
    </form>
  );
}

function TermsForm({ tenant }: { tenant: PlatformTenant }) {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const onSaved = useTenantUpdate();
  const [percent, setPercent] = useState(String(tenant.commissionPercent));
  const [fixed, setFixed] = useState(String(tenant.commissionFixedAmountPerTicket / 100).replace('.', ','));
  const [vatRegistered, setVatRegistered] = useState(tenant.organizerVatRegistered);
  const save = useMutation({
    mutationFn: () =>
      platformApi.updateTerms(tenant.id, {
        commissionPercent: Number(percent.replace(',', '.')),
        commissionFixedAmountPerTicket: parseKronor(fixed) ?? 0,
        organizerVatRegistered: vatRegistered,
      }),
    onSuccess: onSaved,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form className="form panel" onSubmit={submit}>
      <h2>{t('tenant.terms')}</h2>
      <p className="small muted">{t('tenant.termsHelp')}</p>
      <label>
        {t('tenant.commissionPercent')}
        <input inputMode="decimal" required value={percent} onChange={(e) => setPercent(e.target.value)} />
      </label>
      <label>
        {t('tenant.commissionFixed')}
        <input inputMode="decimal" value={fixed} onChange={(e) => setFixed(e.target.value)} />
      </label>
      <label className="checkbox">
        <input type="checkbox" checked={vatRegistered} onChange={(e) => setVatRegistered(e.target.checked)} /> {t('tenant.vatRegistered')}
      </label>
      <button type="submit" className="button button-primary" disabled={save.isPending}>
        {t('tenant.saveTerms')}
      </button>
      {save.isSuccess ? <Notice>{t('tenant.saved')}</Notice> : null}
      {save.isError ? <ErrorMessage>{errorText(save.error)}</ErrorMessage> : null}
    </form>
  );
}

function Domains({ tenant }: { tenant: PlatformTenant }) {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const onSaved = useTenantUpdate();
  const [host, setHost] = useState('');
  const add = useMutation({
    mutationFn: (input: { host: string; primary: boolean }) => platformApi.addDomain(tenant.id, input.host, input.primary),
    onSuccess: (updated) => {
      setHost('');
      onSaved(updated);
    },
  });
  const remove = useMutation({ mutationFn: (domainId: string) => platformApi.removeDomain(tenant.id, domainId), onSuccess: onSaved });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    add.mutate({ host, primary: false });
  };

  return (
    <div className="panel">
      <h2>{t('tenant.domains')}</h2>
      <p className="small muted">{t('tenant.domainsHelp')}</p>
      <ul className="recording-list">
        {tenant.domains.map((d) => (
          <li key={d.id}>
            <code>{d.host}</code>
            <span className="copy-row">
              {d.isPrimary ? <span className="badge">{t('tenant.primary')}</span> : null}
              {tenant.domains.length > 1 ? (
                <button type="button" className="button" onClick={() => remove.mutate(d.id)}>
                  {t('tenant.remove')}
                </button>
              ) : null}
            </span>
          </li>
        ))}
      </ul>
      <form className="form" onSubmit={submit}>
        <label>
          {t('tenant.host')}
          <input required placeholder="stream.example.se" value={host} onChange={(e) => setHost(e.target.value.toLowerCase())} />
        </label>
        <button type="submit" className="button" disabled={add.isPending}>
          {t('tenant.addDomain')}
        </button>
      </form>
      {add.isError ? <ErrorMessage>{errorText(add.error)}</ErrorMessage> : null}
      {remove.isError ? <ErrorMessage>{errorText(remove.error)}</ErrorMessage> : null}
    </div>
  );
}

function InviteAdmin({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const [email, setEmail] = useState('');
  const invite = useMutation({ mutationFn: () => platformApi.inviteAdmin(tenantId, email), onSuccess: () => setEmail('') });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    invite.mutate();
  };

  return (
    <form className="form panel" onSubmit={submit}>
      <h2>{t('tenant.admins')}</h2>
      <label>
        {t('tenant.inviteEmail')}
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <button type="submit" className="button button-primary" disabled={invite.isPending}>
        {t('tenant.invite')}
      </button>
      {invite.isSuccess ? <Notice>{t('tenant.invited')}</Notice> : null}
      {invite.isError ? <ErrorMessage>{errorText(invite.error)}</ErrorMessage> : null}
    </form>
  );
}
