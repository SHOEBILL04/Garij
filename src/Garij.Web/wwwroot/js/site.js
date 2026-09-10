// Shared feedback for state-changing forms. Server-side validation remains
// authoritative; this only prevents accidental duplicate submissions.
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('form[method="post"]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      if (form.dataset.submitting === 'true') {
        event.preventDefault();
        return;
      }
      if (!form.checkValidity()) return;

      form.dataset.submitting = 'true';
      form.setAttribute('aria-busy', 'true');
      const submitter = event.submitter || form.querySelector('button[type="submit"], button:not([type]), input[type="submit"]');
      if (!submitter) return;

      submitter.disabled = true;
      submitter.classList.add('is-loading');
      if (submitter.tagName === 'BUTTON') {
        submitter.innerHTML = '<span class="app-button-spinner" aria-hidden="true"></span><span>Saving…</span>';
      }
    });
  });
});
