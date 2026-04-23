// Browser Notifications helper. Mirrors the Notification API but guards against
// browsers that lack support (iOS Safari, some PWA contexts).
(function () {
    const firedThisSession = new Set();

    function isSupported() {
        return 'Notification' in globalThis;
    }

    globalThis.financeNotifications = {
        isSupported,

        getPermission() {
            return isSupported() ? Notification.permission : 'unsupported';
        },

        async requestPermission() {
            if (!isSupported()) return 'unsupported';
            try {
                const state = await Notification.requestPermission();
                return state;
            } catch {
                return Notification.permission || 'default';
            }
        },

        async show(tag, title, body, url) {
            if (!isSupported()) return false;
            if (Notification.permission !== 'granted') return false;
            if (tag && firedThisSession.has(tag)) return false;

            try {
                const notification = new Notification(title || 'Reminder', {
                    body: body || '',
                    tag: tag || undefined,
                    renotify: false,
                    icon: '/icons/icon-192.png',
                    badge: '/icons/icon-192.png'
                });

                if (url) {
                    notification.onclick = () => {
                        window.focus();
                        try { window.location.assign(url); } catch { /* ignore */ }
                        notification.close();
                    };
                }

                if (tag) firedThisSession.add(tag);
                return true;
            } catch {
                return false;
            }
        },

        resetSessionCache() {
            firedThisSession.clear();
        }
    };
})();
