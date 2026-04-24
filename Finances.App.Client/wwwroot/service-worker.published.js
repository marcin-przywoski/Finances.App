// Caution: offline support makes updates cache-sensitive. Keep a backup flow in the UI.

globalThis.importScripts('./service-worker-assets.js');
globalThis.addEventListener('install', event => event.waitUntil(onInstall()));
globalThis.addEventListener('activate', event => event.waitUntil(onActivate()));
globalThis.addEventListener('fetch', event => event.respondWith(onFetch(event)));
globalThis.addEventListener('message', event => onMessage(event));

const cacheNamePrefix = 'finances-app-offline-';
const cacheName = `${cacheNamePrefix}${globalThis.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.svg$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /^pwa-release\.json$/ ];
const appBaseUrl = new URL('./', globalThis.location.href);
const indexUrl = new URL('index.html', appBaseUrl).href;
const manifestUrlList = globalThis.assetsManifest.assets.map(asset => new URL(asset.url, appBaseUrl).href);

// Long-lived runtime caches for lazy-loaded CDN resources. These are
// deliberately kept across build activations so users don't re-download
// hundreds of megabytes every time we ship a new client build.
const RUNTIME_CACHES = {
    'pdfjs-v1': [/cdn\.jsdelivr\.net\/npm\/pdfjs-dist@/],
    'tesseract-v1': [/cdn\.jsdelivr\.net\/npm\/tesseract\.js@/, /tessdata\.projectnaptha\.com\//],
    'transformers-v1': [
        /cdn\.jsdelivr\.net\/npm\/@xenova\/transformers@/,
        /huggingface\.co\//,
        /cdn-lfs\.huggingface\.co\//
    ]
};
const RUNTIME_CACHE_NAMES = Object.keys(RUNTIME_CACHES);

async function onInstall() {
    console.info('Service worker: install');

    const assetsRequests = globalThis.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(new URL(asset.url, appBaseUrl).href, { integrity: asset.hash, cache: 'no-cache' }));

    const cache = await caches.open(cacheName);
    await cache.addAll(assetsRequests);
}

async function onActivate() {
    console.info('Service worker: activate');

    const preserved = new Set([cacheName, ...RUNTIME_CACHE_NAMES]);
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => !preserved.has(key))
        .filter(key => key.startsWith(cacheNamePrefix) || !RUNTIME_CACHE_NAMES.includes(key))
        .filter(key => key !== cacheName)
        .map(key => caches.delete(key)));

    await globalThis.clients.claim();
}

function matchRuntimeCache(url) {
    for (const [name, patterns] of Object.entries(RUNTIME_CACHES)) {
        if (patterns.some(pattern => pattern.test(url))) return name;
    }
    return null;
}

async function onFetch(event) {
    if (event.request.method !== 'GET') {
        return fetch(event.request);
    }

    const url = event.request.url;
    const runtimeCacheName = matchRuntimeCache(url);
    if (runtimeCacheName) {
        try {
            const runtimeCache = await caches.open(runtimeCacheName);
            const cached = await runtimeCache.match(event.request);
            if (cached) return cached;
            const response = await fetch(event.request);
            if (response && response.status === 200 && response.type !== 'opaqueredirect') {
                runtimeCache.put(event.request, response.clone()).catch(() => { /* quota best-effort */ });
            }
            return response;
        } catch (err) {
            // If network fails and nothing is cached, fall through to a real
            // fetch so the browser surfaces the error to the caller.
            console.warn('[sw] runtime cache fetch failed:', err);
        }
    }

    const cache = await caches.open(cacheName);
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestUrlList.includes(event.request.url);
    const cachedResponse = await cache.match(shouldServeIndexHtml ? indexUrl : event.request);

    return cachedResponse || fetch(event.request);
}

async function clearRuntimeCaches() {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => RUNTIME_CACHE_NAMES.includes(key))
        .map(key => caches.delete(key)));
}

function onMessage(event) {
    if (event.data?.type === 'SKIP_WAITING') {
        globalThis.skipWaiting();
        return;
    }
    if (event.data?.type === 'CLEAR_RUNTIME_CACHES') {
        event.waitUntil(clearRuntimeCaches().then(() => {
            event.source?.postMessage?.({ type: 'RUNTIME_CACHES_CLEARED' });
        }));
    }
}