// In development, always fetch from the network and do not enable offline support.
// This keeps local iteration predictable while the published PWA uses the offline cache.
self.addEventListener('fetch', () => { });