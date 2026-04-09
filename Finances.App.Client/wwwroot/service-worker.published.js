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

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    await globalThis.clients.claim();
}

async function onFetch(event) {
    if (event.request.method !== 'GET') {
        return fetch(event.request);
    }

    const cache = await caches.open(cacheName);
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestUrlList.includes(event.request.url);
    const cachedResponse = await cache.match(shouldServeIndexHtml ? indexUrl : event.request);

    return cachedResponse || fetch(event.request);
}

function onMessage(event) {
    if (event.data?.type === 'SKIP_WAITING') {
        globalThis.skipWaiting();
    }
}
