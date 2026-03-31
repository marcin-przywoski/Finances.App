window.financePwa = (() => {
    let deferredInstallPrompt = null;
    let dotNetHelper = null;
    let readyRegistrationObserved = false;
    let registration = null;
    let trackedInstallingWorker = null;
    let reloadPending = false;

    const onBeforeInstallPrompt = event => {
        event.preventDefault();
        deferredInstallPrompt = event;
        void notifyState();
    };

    const onAppInstalled = () => {
        deferredInstallPrompt = null;
        void notifyState();
    };

    const onControllerChange = () => {
        if (!reloadPending) {
            return;
        }

        window.location.reload();
    };

    const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;

    const invokeStateUpdate = async (installAvailable, updateAvailable) => {
        if (!dotNetHelper) {
            return;
        }

        try {
            await dotNetHelper.invokeMethodAsync('UpdateState', installAvailable, updateAvailable, isStandalone());
        } catch {
            dotNetHelper = null;
        }
    };

    const observeRegistration = reg => {
        if (!reg) {
            return;
        }

        registration = reg;

        if (reg.waiting && navigator.serviceWorker.controller) {
            void notifyState();
        }

        reg.addEventListener('updatefound', () => {
            const installingWorker = reg.installing;
            if (!installingWorker || installingWorker === trackedInstallingWorker) {
                return;
            }

            trackedInstallingWorker = installingWorker;
            installingWorker.addEventListener('statechange', () => {
                if (installingWorker.state === 'installed' && navigator.serviceWorker.controller) {
                    void notifyState();
                }
            });
        });
    };

    const notifyState = async () => {
        const installAvailable = !isStandalone() && deferredInstallPrompt !== null;
        const updateAvailable = Boolean(registration && registration.waiting && navigator.serviceWorker.controller);
        await invokeStateUpdate(installAvailable, updateAvailable);
    };

    const ensureServiceWorkerObservation = async () => {
        if (!('serviceWorker' in navigator)) {
            await notifyState();
            return;
        }

        observeRegistration(await navigator.serviceWorker.getRegistration());

        if (!readyRegistrationObserved) {
            readyRegistrationObserved = true;
            navigator.serviceWorker.ready
                .then(readyRegistration => {
                    observeRegistration(readyRegistration);
                    return notifyState();
                })
                .catch(() => undefined);
        }

        await notifyState();
    };

    return {
        register: async dotNetReference => {
            dotNetHelper = dotNetReference;
            window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt);
            window.addEventListener('appinstalled', onAppInstalled);

            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.addEventListener('controllerchange', onControllerChange);
            }

            await ensureServiceWorkerObservation();
        },

        promptInstall: async () => {
            if (!deferredInstallPrompt) {
                return false;
            }

            const installPrompt = deferredInstallPrompt;
            deferredInstallPrompt = null;

            await installPrompt.prompt();
            const result = await installPrompt.userChoice;

            if (result.outcome !== 'accepted') {
                deferredInstallPrompt = installPrompt;
            }

            await notifyState();
            return result.outcome === 'accepted';
        },

        applyUpdate: async () => {
            await ensureServiceWorkerObservation();

            if (!registration || !registration.waiting) {
                return false;
            }

            reloadPending = true;
            registration.waiting.postMessage({ type: 'SKIP_WAITING' });
            window.setTimeout(() => {
                if (reloadPending) {
                    window.location.reload();
                }
            }, 4000);

            return true;
        },

        dispose: () => {
            window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt);
            window.removeEventListener('appinstalled', onAppInstalled);

            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            }

            dotNetHelper = null;
        }
    };
})();