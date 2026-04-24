// Semantic-search embedding helper. Lazy-loads transformers.js from a pinned
// CDN on first call and caches model weights through the library's built-in
// IndexedDB store. Everything is opt-in: nothing fetches the ~120 MB model
// until the UI explicitly calls ensureModel().
(function () {
    const TRANSFORMERS_VERSION = '2.17.2';
    const TRANSFORMERS_URL = `https://cdn.jsdelivr.net/npm/@xenova/transformers@${TRANSFORMERS_VERSION}/dist/transformers.min.js`;
    const MODEL_ID = 'Xenova/paraphrase-multilingual-MiniLM-L12-v2';

    let libPromise = null;
    let pipelinePromise = null;
    let progressCallbacks = new Set();

    function emitProgress(event) {
        if (!event) return;
        for (const cb of progressCallbacks) {
            try { cb(event); } catch { /* best-effort */ }
        }
    }

    function loadLibrary() {
        if (libPromise) return libPromise;
        libPromise = (async () => {
            const mod = await import(TRANSFORMERS_URL);
            // Default env config is fine: IDB-backed weight cache under
            // "transformers-cache", remote CDN hub, WASM backend.
            if (mod.env) {
                mod.env.allowLocalModels = false;
                mod.env.useBrowserCache = true;
                // Limit number of concurrent model loads so progress events are
                // delivered in-order on slow connections.
                if (mod.env.backends?.onnx?.wasm) {
                    mod.env.backends.onnx.wasm.numThreads = 1;
                }
            }
            return mod;
        })().catch(err => {
            libPromise = null;
            throw err;
        });
        return libPromise;
    }

    async function loadPipeline() {
        if (pipelinePromise) return pipelinePromise;
        pipelinePromise = (async () => {
            const lib = await loadLibrary();
            const pipeline = await lib.pipeline('feature-extraction', MODEL_ID, {
                quantized: true,
                progress_callback: event => emitProgress(event)
            });
            return pipeline;
        })().catch(err => {
            pipelinePromise = null;
            throw err;
        });
        return pipelinePromise;
    }

    function l2Normalize(array) {
        let norm = 0;
        for (let i = 0; i < array.length; i++) norm += array[i] * array[i];
        norm = Math.sqrt(norm);
        if (norm === 0) return array;
        const out = new Float32Array(array.length);
        for (let i = 0; i < array.length; i++) out[i] = array[i] / norm;
        return out;
    }

    async function embedSingle(text) {
        if (!text || !text.trim()) return null;
        const pipeline = await loadPipeline();
        const tensor = await pipeline(text, { pooling: 'mean', normalize: true });
        const data = tensor?.data;
        if (!data) return null;
        return l2Normalize(data instanceof Float32Array ? data : new Float32Array(data));
    }

    globalThis.financeEmbeddings = {
        isSupported() {
            return typeof globalThis.fetch === 'function'
                && typeof globalThis.WebAssembly === 'object'
                && typeof globalThis.indexedDB === 'object';
        },

        registerProgress(callback) {
            if (typeof callback === 'function') progressCallbacks.add(callback);
            return () => progressCallbacks.delete(callback);
        },

        async registerDotNetProgress(dotNetRef) {
            if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') return null;
            const handler = event => {
                try {
                    dotNetRef.invokeMethodAsync('OnProgress',
                        event.status ?? null,
                        typeof event.loaded === 'number' ? event.loaded : 0,
                        typeof event.total === 'number' ? event.total : 0,
                        typeof event.progress === 'number' ? event.progress : 0,
                        event.file ?? null);
                } catch { /* swallowed — dotnet ref may have been disposed */ }
            };
            progressCallbacks.add(handler);
            return {
                dispose() {
                    progressCallbacks.delete(handler);
                }
            };
        },

        async ensureModel() {
            await loadPipeline();
            return { modelId: MODEL_ID, dimension: 384 };
        },

        async isModelReady() {
            return pipelinePromise !== null;
        },

        async embed(text) {
            const vector = await embedSingle(text);
            if (!vector) return null;
            return Array.from(vector);
        },

        async embedBatch(texts) {
            if (!Array.isArray(texts) || texts.length === 0) return [];
            const results = [];
            for (const t of texts) {
                const v = await embedSingle(t);
                results.push(v ? Array.from(v) : null);
            }
            return results;
        },

        cosine(a, b) {
            if (!a || !b || a.length !== b.length) return 0;
            let dot = 0;
            for (let i = 0; i < a.length; i++) dot += a[i] * b[i];
            return dot;
        },

        reset() {
            pipelinePromise = null;
            libPromise = null;
            progressCallbacks = new Set();
        }
    };
})();
