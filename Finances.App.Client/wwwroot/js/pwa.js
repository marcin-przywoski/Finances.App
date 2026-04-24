globalThis.financePwa = (() => {
    const installedVersionStorageKey = 'Finances.App.pwa.installedVersion';
    const installedReleaseStorageKey = 'Finances.App.pwa.installedRelease';
    const pendingUpdateStorageKey = 'Finances.App.pwa.pendingUpdate';
    const lastAppliedUpdateStorageKey = 'Finances.App.pwa.lastAppliedUpdateUtc';
    let deferredInstallPrompt = null;
    let dotNetHelper = null;
    let readyRegistrationObserved = false;
    let browserListenersAttached = false;
    let observedRegistration = null;
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
        currentVersion: globalThis.localStorage.getItem(installedVersionStorageKey),
        availableVersion: null,
        installedRelease: null,
        availableRelease: null,
        lastCheckedUtc: null,
        lastAppliedUpdateUtc: globalThis.localStorage.getItem(lastAppliedUpdateStorageKey)
    };

    const normalizeString = value => typeof value === 'string' && value.trim() ? value.trim() : null;

    const parseStoredJson = storageKey => {
        try {
            const rawValue = globalThis.localStorage.getItem(storageKey);
            return rawValue ? JSON.parse(rawValue) : null;
        } catch {
            return null;
        }
    };

    const normalizeRelease = value => {
        if (!value || typeof value !== 'object') {
            return null;
        }

        const release = {
            releaseId: normalizeString(value.releaseId),
            title: normalizeString(value.title),
            summary: normalizeString(value.summary),
            publishedUtc: normalizeString(value.publishedUtc),
            changes: Array.isArray(value.changes)
                ? value.changes
                    .map(normalizeString)
                    .filter(change => change !== null)
                : []
        };

        if (!release.releaseId && !release.title && !release.summary && !release.publishedUtc && release.changes.length === 0) {
            return null;
        }

        return release;
    };

    const readStoredRelease = () => normalizeRelease(parseStoredJson(installedReleaseStorageKey));

    const writeStoredRelease = release => {
        if (release) {
            globalThis.localStorage.setItem(installedReleaseStorageKey, JSON.stringify(release));
            return;
        }

        globalThis.localStorage.removeItem(installedReleaseStorageKey);
    };

    const readPendingUpdate = () => {
        const pendingUpdate = parseStoredJson(pendingUpdateStorageKey);
        if (!pendingUpdate || typeof pendingUpdate !== 'object') {
            return null;
        }

        const version = normalizeString(pendingUpdate.version);
        const release = normalizeRelease(pendingUpdate.release);
        return version || release ? { version, release } : null;
    };

    const writePendingUpdate = metadata => {
        globalThis.localStorage.setItem(pendingUpdateStorageKey, JSON.stringify({
            version: metadata?.version ?? null,
            release: metadata?.release ?? null
        }));
    };

    const clearPendingUpdate = () => {
        globalThis.localStorage.removeItem(pendingUpdateStorageKey);
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

    const onControllerChange = async () => {
        const latestMetadata = await loadLatestMetadata();
        const justUpdated = completePendingUpdate(latestMetadata);
        await syncState(latestMetadata, justUpdated);

        if (reloadPending) {
            globalThis.location.reload();
        }
    };

    const onVisibilityChange = () => {
        if (globalThis.document.visibilityState === 'visible') {
            void checkForUpdates(true);
        }
    };

    const ensureBrowserEventListeners = () => {
        if (browserListenersAttached) {
            return;
        }

        browserListenersAttached = true;
        globalThis.addEventListener('beforeinstallprompt', onBeforeInstallPrompt);
        globalThis.addEventListener('appinstalled', onAppInstalled);
        globalThis.document.addEventListener('visibilitychange', onVisibilityChange);

        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.addEventListener('controllerchange', onControllerChange);
        }
    };

    const removeBrowserEventListeners = () => {
        if (!browserListenersAttached) {
            return;
        }

        browserListenersAttached = false;
        globalThis.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt);
        globalThis.removeEventListener('appinstalled', onAppInstalled);
        globalThis.document.removeEventListener('visibilitychange', onVisibilityChange);

        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
        }
    };

    const isStandalone = () => globalThis.matchMedia('(display-mode: standalone)').matches || globalThis.navigator.standalone === true;

    const extractVersion = source => {
        const match = source.match(/"version"\s*:\s*"([^"]+)"/i);
        return match ? match[1] : null;
    };

    const loadLatestVersion = async () => {
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

    const loadLatestRelease = async () => {
        try {
            const response = await fetch('pwa-release.json', { cache: 'no-cache' });
            if (!response.ok) {
                return null;
            }

            return normalizeRelease(await response.json());
        } catch {
            return null;
        }
    };

    const loadLatestMetadata = async () => ({
        version: await loadLatestVersion(),
        release: await loadLatestRelease()
    });

    const persistInstalledMetadata = (metadata, markApplied) => {
        if (metadata?.version) {
            globalThis.localStorage.setItem(installedVersionStorageKey, metadata.version);
        }

        writeStoredRelease(metadata?.release ?? null);

        if (!markApplied) {
            return false;
        }

        const appliedAt = new Date().toISOString();
        globalThis.localStorage.setItem(lastAppliedUpdateStorageKey, appliedAt);
        state.lastAppliedUpdateUtc = appliedAt;
        return true;
    };

    const hydrateInitialInstall = latestMetadata => {
        const installedVersion = globalThis.localStorage.getItem(installedVersionStorageKey);
        if (installedVersion || !navigator.serviceWorker?.controller || !latestMetadata.version) {
            return;
        }

        persistInstalledMetadata(latestMetadata, false);
    };

    const completePendingUpdate = latestMetadata => {
        const pendingUpdate = readPendingUpdate();
        if (!pendingUpdate || !navigator.serviceWorker?.controller || registration?.waiting) {
            return false;
        }

        const effectiveVersion = latestMetadata.version ?? pendingUpdate.version;
        const effectiveRelease = latestMetadata.release ?? pendingUpdate.release;
        const installedVersion = globalThis.localStorage.getItem(installedVersionStorageKey);

        clearPendingUpdate();

        if (!effectiveVersion || effectiveVersion === installedVersion) {
            return false;
        }

        return persistInstalledMetadata({ version: effectiveVersion, release: effectiveRelease }, true);
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
        if (reg === observedRegistration) {
            return;
        }

        observedRegistration = reg;

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

    const syncState = async (latestMetadataOverride, forcedJustUpdated = false) => {
        const latestMetadata = latestMetadataOverride ?? await loadLatestMetadata();
        hydrateInitialInstall(latestMetadata);

        const updateAvailable = Boolean(registration?.waiting && navigator.serviceWorker.controller);
        state = {
            ...state,
            installAvailable: !isStandalone() && deferredInstallPrompt !== null,
            updateAvailable,
            isStandalone: isStandalone(),
            isOfflineReady: Boolean(navigator.serviceWorker?.controller || registration?.active),
            justUpdated: forcedJustUpdated,
            currentVersion: globalThis.localStorage.getItem(installedVersionStorageKey),
            availableVersion: updateAvailable ? latestMetadata.version : null,
            installedRelease: readStoredRelease(),
            availableRelease: updateAvailable ? latestMetadata.release : null,
            lastAppliedUpdateUtc: globalThis.localStorage.getItem(lastAppliedUpdateStorageKey)
        };

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
            return await waitForUpdateResult(silent ? 2500 : 10000);
        } finally {
            state = {
                ...state,
                isCheckingForUpdates: false
            };

            await syncState();
        }
    };

    ensureBrowserEventListeners();

    return {
        register: async dotNetReference => {
            dotNetHelper = dotNetReference;
            ensureBrowserEventListeners();

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

            const latestMetadata = await loadLatestMetadata();
            writePendingUpdate(latestMetadata);
            reloadPending = true;
            registration.waiting.postMessage({ type: 'SKIP_WAITING' });
            return true;
        },

        clearRuntimeCaches: async () => {
            // Runtime caches (pdfjs-v1, tesseract-v1, transformers-v1) are
            // preserved across SW activations. We clear them via the SW so the
            // next time those libraries are needed they're re-downloaded.
            try {
                if ('serviceWorker' in navigator && navigator.serviceWorker.controller) {
                    await new Promise(resolve => {
                        const channel = new MessageChannel();
                        const timeout = globalThis.setTimeout(() => resolve(), 3000);
                        channel.port1.onmessage = event => {
                            if (event?.data?.type === 'RUNTIME_CACHES_CLEARED') {
                                globalThis.clearTimeout(timeout);
                                resolve();
                            }
                        };
                        navigator.serviceWorker.controller.postMessage({ type: 'CLEAR_RUNTIME_CACHES' }, [channel.port2]);
                    });
                } else if ('caches' in globalThis) {
                    // SW not active — delete the known runtime cache names directly.
                    const keys = await caches.keys();
                    const runtime = ['pdfjs-v1', 'tesseract-v1', 'transformers-v1'];
                    await Promise.all(keys.filter(k => runtime.includes(k)).map(k => caches.delete(k)));
                }
                // Also wipe the transformers.js IDB weight cache so freshly
                // downloaded files repopulate it.
                if ('indexedDB' in globalThis && typeof indexedDB.deleteDatabase === 'function') {
                    try { indexedDB.deleteDatabase('transformers-cache'); } catch { /* best-effort */ }
                }
                if (globalThis.financeEmbeddings?.reset) {
                    try { globalThis.financeEmbeddings.reset(); } catch { /* best-effort */ }
                }
                return true;
            } catch {
                return false;
            }
        },

        dispose: () => {
            removeBrowserEventListeners();

            dotNetHelper = null;
        }
    };
})();
