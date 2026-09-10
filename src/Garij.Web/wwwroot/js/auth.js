/**
 * Garij Auth Pages JavaScript
 * Feature: show/hide toggle for password fields
 */
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.auth-toggle-password').forEach((btn) => {
        btn.addEventListener('click', () => {
            const targetId = btn.getAttribute('data-target');
            const input = targetId && document.getElementById(targetId);
            if (!input) {
                return;
            }

            const icon = btn.querySelector('i');
            const showing = input.type === 'password';
            input.type = showing ? 'text' : 'password';
            icon.classList.toggle('bi-eye-slash', !showing);
            icon.classList.toggle('bi-eye', showing);
            btn.setAttribute('aria-label', showing ? 'Hide password' : 'Show password');
        });
    });
});
