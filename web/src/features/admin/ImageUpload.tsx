import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { adminApi } from '../../api/admin';
import { ErrorMessage } from '../../shared/components';
import { useErrorText } from '../../shared/errors';

/** Uploads an image and reports its URL (event images, logo). */
export function ImageUpload({ onUploaded }: { onUploaded: (url: string) => void }) {
  const { t } = useTranslation('admin');
  const errorText = useErrorText();
  const upload = useMutation({ mutationFn: (file: File) => adminApi.uploadImage(file), onSuccess: (r) => onUploaded(r.url) });

  return (
    <div>
      <label className="button">
        {upload.isPending ? t('upload.uploading') : t('upload.choose')}
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) upload.mutate(file);
            e.target.value = '';
          }}
        />
      </label>
      <p className="small muted">{t('upload.help')}</p>
      {upload.isError ? <ErrorMessage>{errorText(upload.error)}</ErrorMessage> : null}
    </div>
  );
}
