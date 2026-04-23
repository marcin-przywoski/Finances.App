// PDF text extraction helper. Lazy-loads PDF.js from a CDN the first time a
// user attaches a PDF and returns a plain-text dump plus page count. Failures
// (offline, unsupported file, password-protected) degrade silently to null so
// the caller can fall back to manual entry.
(function () {
    const VERSION = '4.8.69';
    const CDN_BASE = `https://cdn.jsdelivr.net/npm/pdfjs-dist@${VERSION}/build/`;
    let pdfjsPromise = null;

    function loadPdfJs() {
        if (pdfjsPromise) return pdfjsPromise;
        pdfjsPromise = (async () => {
            const pdfjs = await import(CDN_BASE + 'pdf.min.mjs');
            if (pdfjs.GlobalWorkerOptions && !pdfjs.GlobalWorkerOptions.workerSrc) {
                pdfjs.GlobalWorkerOptions.workerSrc = CDN_BASE + 'pdf.worker.min.mjs';
            }
            return pdfjs;
        })().catch(err => {
            pdfjsPromise = null;
            throw err;
        });
        return pdfjsPromise;
    }

    function base64ToBytes(base64) {
        const binary = globalThis.atob(base64 || '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    globalThis.financePdfExtract = {
        isSupported() {
            return typeof globalThis.atob === 'function' && 'fetch' in globalThis;
        },

        async extractText(base64Content, maxPages) {
            if (!base64Content) return null;
            try {
                const pdfjs = await loadPdfJs();
                const data = base64ToBytes(base64Content);
                const loadingTask = pdfjs.getDocument({ data });
                const pdf = await loadingTask.promise;
                const pageLimit = Math.max(1, Math.min(pdf.numPages, maxPages || 5));
                const parts = [];
                for (let i = 1; i <= pageLimit; i++) {
                    const page = await pdf.getPage(i);
                    const content = await page.getTextContent();
                    // Concatenate text items in reading order. PDF.js already
                    // returns them roughly top-to-bottom for most invoices.
                    parts.push(content.items.map(item => item.str).join(' '));
                }
                return { text: parts.join('\n'), pageCount: pdf.numPages };
            } catch (err) {
                console.warn('[pdf-extract] failed:', err?.message || err);
                return null;
            }
        }
    };
})();
