import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { platformApi } from '../../api/platform';
import { ErrorMessage, Loading } from '../../shared/components';
import { useErrorText } from '../../shared/errors';

export function TenantsPage() {
  const { t } = useTranslation('platform');
  const errorText = useErrorText();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const tenants = useQuery({ queryKey: ['platform-tenants'], queryFn: platformApi.tenants });
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [supportEmail, setSupportEmail] = useState('');
  const create = useMutation({
    mutationFn: () => platformApi.createTenant({ name, slug, supportEmail: supportEmail || undefined }),
    onSuccess: async (tenant) => {
      await queryClient.invalidateQueries({ queryKey: ['platform-tenants'] });
      void navigate(`/arrangorer/${tenant.id}`);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <section>
      <h1>{t('tenants.title')}</h1>
      {tenants.isPending ? <Loading /> : null}
      {tenants.isError ? <ErrorMessage>{errorText(tenants.error)}</ErrorMessage> : null}
      {tenants.data?.length === 0 ? <p className="muted">{t('tenants.empty')}</p> : null}
      <ul className="admin-list">
        {tenants.data?.map((tenant) => (
          <li key={tenant.id}>
            <Link to={`/arrangorer/${tenant.id}`} className="admin-list-item">
              <div>
                <h2>{tenant.name}</h2>
                <p className="small muted">{tenant.domains.map((d) => d.host).join(', ')}</p>
              </div>
              {tenant.status === 'suspended' ? <span className="badge">{t('tenants.suspended')}</span> : null}
            </Link>
          </li>
        ))}
      </ul>
      <form className="form panel narrow" onSubmit={submit}>
        <h2>{t('tenants.create')}</h2>
        <label>
          {t('tenants.name')}
          <input required value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>
          {t('tenants.slug', { slug: slug || '…' })}
          <input required pattern="[a-z0-9]+(-[a-z0-9]+)*" value={slug} onChange={(e) => setSlug(e.target.value.toLowerCase())} />
        </label>
        <label>
          {t('tenants.supportEmail')}
          <input type="email" value={supportEmail} onChange={(e) => setSupportEmail(e.target.value)} />
        </label>
        <button type="submit" className="button button-primary" disabled={create.isPending}>
          {t('tenants.save')}
        </button>
        {create.isError ? <ErrorMessage>{errorText(create.error)}</ErrorMessage> : null}
      </form>
    </section>
  );
}
