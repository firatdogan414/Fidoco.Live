import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { adminApi, type EventInput } from '../../api/admin';
import { EventForm } from './EventForm';

export function AdminNewEventPage() {
  const { t } = useTranslation('admin');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useMutation({
    mutationFn: (input: EventInput) => adminApi.createEvent(input),
    onSuccess: async (ev) => {
      await queryClient.invalidateQueries({ queryKey: ['admin-events'] });
      void navigate(`/admin/event/${ev.id}`, { replace: true });
    },
  });

  return (
    <section className="narrow">
      <h1>{t('form.newTitle')}</h1>
      <EventForm submitting={create.isPending} error={create.error} onSubmit={(input) => create.mutate(input)} />
    </section>
  );
}
