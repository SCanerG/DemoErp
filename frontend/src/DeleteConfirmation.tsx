import { t } from './i18n';
import { useEffect, useRef } from 'react';
import { ErrorNotice } from './components';

export function DeleteConfirmation({ product, pending, error, onCancel, onDelete, record = false }: {
  product: { name: string };
  record?: boolean;
  pending: boolean;
  error: unknown;
  onCancel: () => void;
  onDelete: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    dialog?.showModal();
    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, []);

  return <dialog ref={ref} aria-labelledby="delete-title" aria-describedby="delete-description"
    className="confirmation-dialog panel" onKeyDown={event => {
      if (event.key !== 'Tab') return;
      const buttons = event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
      const first = buttons[0];
      const last = buttons[buttons.length - 1];
      if (!first || !last) { event.preventDefault(); return; }
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    }} onCancel={event => {
      event.preventDefault();
      if (!pending) onCancel();
    }}>
    <div className="mb-5 flex size-11 items-center justify-center rounded-full bg-red-50 text-xl text-red-700" aria-hidden="true">×</div>
    <h2 id="delete-title" className="text-xl font-semibold">{t(record ? 'Delete record?' : 'Delete product?')}</h2>
    <p id="delete-description" className="mb-6 mt-3 break-words leading-6 text-slate-600">“{product.name}{record ? `” ${t('This action cannot be undone.')}` : t("” will be permanently removed from the catalog. This action cannot be undone.")}</p>
    <ErrorNotice error={error} />
    <div className="mt-6 flex justify-end gap-3">
      <button autoFocus className="button secondary" disabled={pending} onClick={onCancel}>{t("Cancel")}</button>
      <button className="button danger" disabled={pending} onClick={onDelete}>{pending ? t("Deleting…") : t(record ? 'Delete' : 'Delete product')}</button>
    </div>
  </dialog>;
}
