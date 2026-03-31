globalThis.financePwa = (() => {
    const versionStorageKey = 'kubiczplace.finances.pwa.currentVersion';
    const lastAppliedUpdateStorageKey = 'kubiczplace.finances.pwa.lastAppliedUpdateUtc';
    let deferredInstallPrompt = null;
    let dotNetHelper = null;
    let readyRegistrationObserved = false;
    let registration = null;
    let trackedInstallingWorker = null;
    let reloadPending = false;
    let state = {
        installAvailable: false,
        updateAvailable: false,
        isStandalone: false,
        isOfflineReady: false,
        isCheckingForUpdates: false,
        justUpdated: false,
        currentVersion: null,
        lastCheckedUtc: null,
        lastAppliedUpdateUtc: globalThis.localStorage.getItem(lastAppliedUpdateStorageKey)
    };

    const onBeforeInstallPrompt = event => {
        event.preventDefault();
        deferredInstallPrompt = event;
        void syncState();
    };

    const onAppInstalled = () => {
        deferredInstallPrompt = null;
        void syncState();
    };

    const onControllerChange = () => {
        if (!reloadPending) {
            return;
        }

        globalThis.location.reload();
    };

    const onVisibilityChange = () => {
        if (globalThis.document.visibilityState === 'visible') {
            void checkForUpdates(true);
        }
    };

    const isStandalone = () => globalThis.matchMedia('(display-mode: standalone)').matches || globalThis.navigator.standalone === true;

    const extractVersion = source => {
        const match = source.match(/"version"\s*:\s*"([^"]+)"/i);
        return match ? match[1] : null;
    };

    const loadCurrentVersion = async () => {
        try {
            const response = await fetch('service-worker-assets.js', { cache: 'no-cache' });
            if (!response.ok) {
                return null;
            }

            return extractVersion(await response.text());
        } catch {
            return null;
        }
    };

    const trackAppliedVersion = version => {
        if (!version) {
            state.justUpdated = false;
            return;
        }

        const previousVersion = globalThis.localStorage.getItem(versionStorageKey);
        if (previousVersion && previousVersion !== version) {
            const now = new Date().toISOString();
            state.justUpdated = true;
            state.lastAppliedUpdateUtc = now;
            globalThis.localStorage.setItem(lastAppliedUpdateStorageKey, now);
        } else {
            state.justUpdated = false;
            state.lastAppliedUpdateUtc = globalThis.localStorage.getItem(lastAppliedUpdateStorageKey);
        }

        globalThis.localStorage.setItem(versionStorageKey, version);
    };

    const invokeStateUpdate = async () => {
        if (!dotNetHelper) {
            return;
        }

        try {
            await dotNetHelper.invokeMethodAsync('UpdateState', JSON.stringify(state));
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
            void syncState();
        }

        reg.addEventListener('updatefound', () => {
            const installingWorker = reg.installing;
            if (!installingWorker || installingWorker === trackedInstallingWorker) {
                return;
            }

            trackedInstallingWorker = installingWorker;
            installingWorker.addEventListener('statechange', () => {
                if (installingWorker.state === 'installed' && navigator.serviceWorker.controller) {
                    void syncState();
                }
            });
        });
    };

    const syncState = async () => {
        state = {
            ...state,
            installAvailable: !isStandalone() && deferredInstallPrompt !== null,
            updateAvailable: Boolean(registration?.waiting && navigator.serviceWorker.controller),
            isStandalone: isStandalone(),
            isOfflineReady: Boolean(navigator.serviceWorker?.controller),
            currentVersion: await loadCurrentVersion()
        };

        trackAppliedVersion(state.currentVersion);
        await invokeStateUpdate();
    };

    const ensureServiceWorkerObservation = async () => {
        if (!('serviceWorker' in navigator)) {
            await syncState();
            return;
        }

        observeRegistration(await navigator.serviceWorker.getRegistration());

        if (!readyRegistrationObserved) {
            readyRegistrationObserved = true;
            navigator.serviceWorker.ready
                .then(readyRegistration => {
                    observeRegistration(readyRegistration);
                    return syncState();
                })
                .catch(() => undefined);
        }

        await syncState();
    };

    const watchInstallingWorker = (installingWorker, onComplete) => {
        if (!installingWorker) {
            return;
        }

        const handleStateChange = () => {
            if (installingWorker.state === 'installed' || installingWorker.state === 'redundant') {
                installingWorker.removeEventListener('statechange', handleStateChange);
                onComplete();
            }
        };

        installingWorker.addEventListener('statechange', handleStateChange);
    };

    const waitForUpdateResult = async timeoutMs => {
        if (!registration) {
            return false;
        }

        if (registration.waiting) {
            return true;
        }

        await new Promise(resolve => {
            let completed = false;
            let timeoutId = 0;

            const finish = () => {
                if (completed) {
                    return;
                }

                completed = true;
                registration?.removeEventListener('updatefound', handleUpdateFound);
                globalThis.clearTimeout(timeoutId);
                resolve();
            };

            const handleUpdateFound = () => {
                watchInstallingWorker(registration?.installing, finish);
            };

            registration.addEventListener('updatefound', handleUpdateFound);
            watchInstallingWorker(registration.installing, finish);
            timeoutId = globalThis.setTimeout(finish, timeoutMs);
        });

        return Boolean(registration.waiting);
    };

    const checkForUpdates = async silent => {
        state = {
            ...state,
            isCheckingForUpdates: true,
            lastCheckedUtc: new Date().toISOString()
        };

        await invokeStateUpdate();

        try {
            if (!('serviceWorker' in navigator)) {
                return false;
            }

            await ensureServiceWorkerObservation();

            if (!registration) {
                return false;
            }

            await registration.update();
            return await waitForUpdateResult(silent ? 1500 : 4000);
        } finally {
            state = {
                ...state,
                isCheckingForUpdates: false
            };

            await syncState();
        }
    };

    return {
        register: async dotNetReference => {
            dotNetHelper = dotNetReference;
            globalThis.addEventListener('beforeinstallprompt', onBeforeInstallPrompt);
            globalThis.addEventListener('appinstalled', onAppInstalled);
            globalThis.document.addEventListener('visibilitychange', onVisibilityChange);

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

            await syncState();
            return result.outcome === 'accepted';
        },

        checkForUpdates: async () => {
            return await checkForUpdates(false);
        },

        applyUpdate: async () => {
            await ensureServiceWorkerObservation();

            if (!registration?.waiting) {
                return false;
            }

            reloadPending = true;
            registration.waiting.postMessage({ type: 'SKIP_WAITING' });
            globalThis.setTimeout(() => {
                if (reloadPending) {
                    globalThis.location.reload();
                }
            }, 4000);

            return true;
        },

        dispose: () => {
            globalThis.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt);
            globalThis.removeEventListener('appinstalled', onAppInstalled);
            globalThis.document.removeEventListener('visibilitychange', onVisibilityChange);

            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            }

            dotNetHelper = null;
        }
    };
})();