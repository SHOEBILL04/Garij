/**
 * GARIJ - Theme Management Engine
 * Handles Light Mode and Dark Mode toggling with instant localStorage persistence.
 */
(function () {
    'use strict';

    const STORAGE_KEY = 'garij_theme';
    const THEME_DARK = 'dark';
    const THEME_LIGHT = 'light';

    function getSavedTheme() {
        return localStorage.getItem(STORAGE_KEY) || THEME_DARK;
    }

    function updateToggleButtonUi(theme) {
        const toggleButtons = document.querySelectorAll('[data-theme-toggle], .theme-toggle-btn');
        toggleButtons.forEach(btn => {
            const icon = btn.querySelector('.theme-toggle-icon');
            const label = btn.querySelector('.theme-toggle-text');

            if (theme === THEME_LIGHT) {
                if (icon) {
                    icon.className = 'bi bi-moon-stars-fill theme-toggle-icon text-primary';
                }
                if (label) {
                    label.textContent = 'Dark';
                }
                btn.setAttribute('title', 'Switch to Dark Mode');
                btn.setAttribute('aria-label', 'Switch to Dark Mode');
            } else {
                if (icon) {
                    icon.className = 'bi bi-sun-fill theme-toggle-icon text-warning';
                }
                if (label) {
                    label.textContent = 'Light';
                }
                btn.setAttribute('title', 'Switch to Light Mode');
                btn.setAttribute('aria-label', 'Switch to Light Mode');
            }
        });
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem(STORAGE_KEY, theme);
        updateToggleButtonUi(theme);

        // Dispatch a custom event so other components can react if needed
        window.dispatchEvent(new CustomEvent('garij:themechanged', { detail: { theme } }));
    }

    function toggleTheme() {
        const currentTheme = document.documentElement.getAttribute('data-theme') || THEME_DARK;
        const nextTheme = currentTheme === THEME_DARK ? THEME_LIGHT : THEME_DARK;
        applyTheme(nextTheme);
    }

    // Expose toggle function globally
    window.garijToggleTheme = toggleTheme;

    // Run on DOM ready to bind buttons
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initThemeControls);
    } else {
        initThemeControls();
    }

    function initThemeControls() {
        const activeTheme = document.documentElement.getAttribute('data-theme') || getSavedTheme();
        updateToggleButtonUi(activeTheme);

        document.addEventListener('click', function (e) {
            const toggleBtn = e.target.closest('[data-theme-toggle], .theme-toggle-btn');
            if (toggleBtn) {
                e.preventDefault();
                toggleTheme();
            }
        });
    }
})();
