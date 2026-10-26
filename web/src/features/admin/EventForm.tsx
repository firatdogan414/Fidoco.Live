import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { AdminEvent, EventInput } from '../../api/admin';
import { ApiError } from '../../api/client';
import { ErrorMessage } from '../../shared/components';
import { useErrorText } from '../../shared/errors';
import { fromLocalInput, parseKronor, toLocalInput } from '../../shared/format';

interface Props {
  initial?: AdminEvent;
  submitting: boolean;
  error: unknown;
  onSubmit: (input: EventInput) => void;
}

/** Create/edit form for an event and its (single, phase 1) ticket type. */
export function EventForm({ initial, submitting, error, onSubmit }: Props) {
  const { t } = useTranslation('admin');
  const { t: tc } = useTranslation();
  const errorText = useErrorText();
  const [title, setTitle] = useState(initial?.title ?? '');
  const [slug, setSlug] = useState(initial?.slug ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [imageUrl, setImageUrl] = useState(initial?.imageUrl ?? '');
  const [startsAt, setStartsAt] = useState(initial ? toLocalInput(initial.startsAt) : '');
  const [ticketName, setTicketName] = useState(initial?.ticket?.name ?? 'Livestream');
  const [price, setPrice] = useState(initial?.ticket ? String(initial.ticket.priceAmount / 100).replace('.', ',') : '199');
  const [replayDays, setReplayDays] = useState(initial?.ticket?.replayAccessDays?.toString() ?? '7');
  const [priceError, setPriceError] = useState(false);

  const fieldErrors = error instanceof ApiError ? error.fieldErrors : {};
  const fieldError = (field: string) =>
    fieldErrors[field]?.length ? <span className="field-error">{tc(`errors.${fieldErrors[field][0]}`)}</span> : null;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const priceAmount = parseKronor(price);
    if (priceAmount === null || priceAmount < 300) {
      setPriceError(true);
      return;
    }
    setPriceError(false);
    onSubmit({
      title,
      slug: slug || undefined,
      description: description || undefined,
      imageUrl: imageUrl || undefined,
      startsAt: fromLocalInput(startsAt),
      ticket: { name: ticketName, priceAmount, replayAccessDays: replayDays ? Number(replayDays) : null },
    });
  };

  return (
    <form onSubmit={submit} className="form">
      <label>
        {t('form.title')}
        <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        {fieldError('title')}
      </label>
      <label>
        {t('form.slug')}
        <input maxLength={80} pattern="[a-z0-9]+(-[a-z0-9]+)*" value={slug} onChange={(e) => setSlug(e.target.value)} />
        {fieldError('slug')}
      </label>
      <label>
        {t('form.startsAt')}
        <input type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
        {fieldError('startsAt')}
      </label>
      <label>
        {t('form.description')}
        <textarea rows={5} maxLength={5000} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      <label>
        {t('form.imageUrl')}
        <input type="url" maxLength={500} value={imageUrl} onChange={(e) => setImageUrl(e.target.value)} />
        {fieldError('imageUrl')}
      </label>
      <fieldset className="fieldset">
        <label>
          {t('form.ticketName')}
          <input required maxLength={100} value={ticketName} onChange={(e) => setTicketName(e.target.value)} />
        </label>
        <label>
          {t('form.price')}
          <input inputMode="decimal" required value={price} onChange={(e) => setPrice(e.target.value)} />
          {priceError ? <span className="field-error">{t('form.invalidPrice')}</span> : fieldError('ticket.priceAmount')}
        </label>
        <label>
          {t('form.replayDays')}
          <input type="number" min={1} max={365} value={replayDays} onChange={(e) => setReplayDays(e.target.value)} />
          {fieldError('ticket.replayAccessDays')}
        </label>
      </fieldset>
      <button type="submit" className="button button-primary" disabled={submitting}>
        {t('form.save')}
      </button>
      {error && !(error instanceof ApiError && error.code === 'validation_failed') ? <ErrorMessage>{errorText(error)}</ErrorMessage> : null}
    </form>
  );
}
