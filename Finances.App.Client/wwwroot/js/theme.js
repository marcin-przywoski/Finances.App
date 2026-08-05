// Owns the Finances.App.theme storage key. The user preference is
// 'light' | 'dark' | 'auto' (auto = follow the OS setting); the effective
// mode is applied as data-bs-theme on <html>, which drives both Bootstrap's
// built-in dark styles and the app's token overrides. index.html applies the
// stored preference inline before first paint; this module owns changes at
// runtime.
window.financeTheme = (() => {
    const KEY = 'Finances.App.theme';
    const media = window.matchMedia('(prefers-color-scheme: dark)');

    function stored() {
        try {
            const value = localStorage.getItem(KEY);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch {
            return 'auto';
        }
    }

    function effective(preference) {
        return preference === 'auto' ? (media.matches ? 'dark' : 'light') : preference;
    }

    function apply() {
        const mode = effective(stored());
        document.documentElement.setAttribute('data-bs-theme', mode);
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute('content', mode === 'dark' ? '#134e4a' : '#0d9488');
        }
    }

    media.addEventListener('change', () => {
        if (stored() === 'auto') {
            apply();
        }
    });

    return {
        get: stored,
        set: preference => {
            try {
                if (preference === 'light' || preference === 'dark') {
                    localStorage.setItem(KEY, preference);
                } else {
                    localStorage.removeItem(KEY);
                }
            } catch {
                // Applying still works for this session even if storage fails.
            }
            apply();
        },
        apply
    };
})();
