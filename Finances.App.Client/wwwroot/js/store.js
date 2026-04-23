// Minimal IndexedDB wrapper for Finances.App.
// Object stores:
//   - attachments: key = string id, value = { mime, blob, originalFileName, sizeBytes, createdUtc, entityType, entityId }
//   - embeddings:  key = "entityType:entityId:fieldName", value = { vector: Float32Array, hash: string, updatedUtc: string }
// Exposed as globalThis.financeStore.

(function () {
    const DB_NAME = 'Finances.App';
    const DB_VERSION = 1;
    const ATTACHMENTS_STORE = 'attachments';
    const EMBEDDINGS_STORE = 'embeddings';

    let dbPromise = null;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            if (!('indexedDB' in globalThis)) {
                reject(new Error('IndexedDB is not available in this browser.'));
                return;
            }
            const request = globalThis.indexedDB.open(DB_NAME, DB_VERSION);
            request.onupgradeneeded = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(ATTACHMENTS_STORE)) db.createObjectStore(ATTACHMENTS_STORE);
                if (!db.objectStoreNames.contains(EMBEDDINGS_STORE)) db.createObjectStore(EMBEDDINGS_STORE);
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error ?? new Error('IndexedDB open failed.'));
            request.onblocked = () => reject(new Error('IndexedDB open blocked.'));
        });
        return dbPromise;
    }

    function runTransaction(storeName, mode, callback) {
        return openDb().then(db => new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            const store = tx.objectStore(storeName);
            let result;
            try { result = callback(store); }
            catch (e) { reject(e); return; }
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error ?? new Error('IndexedDB transaction failed.'));
            tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted.'));
        }));
    }

    function asPromise(request) {
        return new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed.'));
        });
    }

    function base64ToBytes(base64) {
        const binary = globalThis.atob(base64 || '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    function bytesToBase64(bytes) {
        let binary = '';
        const chunkSize = 0x8000;
        for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + chunkSize, bytes.length)));
        }
        return globalThis.btoa(binary);
    }

    globalThis.financeStore = {
        async putAttachment(id, mime, base64Content, originalFileName, entityType, entityId) {
            if (!id) throw new Error('Attachment id is required.');
            const bytes = base64ToBytes(base64Content);
            const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
            const record = {
                mime: mime || 'application/octet-stream',
                blob,
                originalFileName: originalFileName || null,
                sizeBytes: bytes.length,
                createdUtc: new Date().toISOString(),
                entityType: entityType || null,
                entityId: typeof entityId === 'number' ? entityId : null
            };
            await runTransaction(ATTACHMENTS_STORE, 'readwrite', store => store.put(record, id));
            return { id, sizeBytes: bytes.length, mime: record.mime, createdUtc: record.createdUtc };
        },

        async getAttachment(id) {
            if (!id) return null;
            const record = await runTransaction(ATTACHMENTS_STORE, 'readonly', store => asPromise(store.get(id)));
            if (!record) return null;
            const buffer = await record.blob.arrayBuffer();
            return {
                id,
                mime: record.mime,
                originalFileName: record.originalFileName,
                sizeBytes: record.sizeBytes,
                createdUtc: record.createdUtc,
                entityType: record.entityType,
                entityId: record.entityId,
                base64: bytesToBase64(new Uint8Array(buffer))
            };
        },

        async getAttachmentBlobUrl(id) {
            if (!id) return null;
            const record = await runTransaction(ATTACHMENTS_STORE, 'readonly', store => asPromise(store.get(id)));
            if (!record) return null;
            return URL.createObjectURL(record.blob);
        },

        async deleteAttachment(id) {
            if (!id) return false;
            await runTransaction(ATTACHMENTS_STORE, 'readwrite', store => store.delete(id));
            return true;
        },

        async listAttachmentIds() {
            return await runTransaction(ATTACHMENTS_STORE, 'readonly', store => asPromise(store.getAllKeys()));
        },

        async getAttachmentsSummary() {
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(ATTACHMENTS_STORE, 'readonly');
                const store = tx.objectStore(ATTACHMENTS_STORE);
                const items = [];
                const req = store.openCursor();
                req.onsuccess = () => {
                    const cursor = req.result;
                    if (!cursor) { resolve(items); return; }
                    const v = cursor.value;
                    items.push({
                        id: cursor.key, mime: v.mime, originalFileName: v.originalFileName,
                        sizeBytes: v.sizeBytes, createdUtc: v.createdUtc,
                        entityType: v.entityType, entityId: v.entityId
                    });
                    cursor.continue();
                };
                req.onerror = () => reject(req.error ?? new Error('Failed to list attachments.'));
            });
        },

        async putEmbedding(key, vectorArray, hash) {
            if (!key) throw new Error('Embedding key is required.');
            const vector = vectorArray instanceof Float32Array ? vectorArray : new Float32Array(vectorArray || []);
            await runTransaction(EMBEDDINGS_STORE, 'readwrite', store => store.put({
                vector, hash: hash || null, updatedUtc: new Date().toISOString()
            }, key));
            return true;
        },

        async getEmbeddingHash(key) {
            if (!key) return null;
            const record = await runTransaction(EMBEDDINGS_STORE, 'readonly', store => asPromise(store.get(key)));
            return record ? record.hash : null;
        },

        async deleteEmbedding(key) {
            if (!key) return false;
            await runTransaction(EMBEDDINGS_STORE, 'readwrite', store => store.delete(key));
            return true;
        },

        async deleteEmbeddingsByPrefix(prefix) {
            if (!prefix) return 0;
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(EMBEDDINGS_STORE, 'readwrite');
                const store = tx.objectStore(EMBEDDINGS_STORE);
                const range = IDBKeyRange.bound(prefix, prefix + '\uffff', false, false);
                const req = store.openCursor(range);
                let deleted = 0;
                req.onsuccess = () => {
                    const cursor = req.result;
                    if (!cursor) return;
                    cursor.delete();
                    deleted++;
                    cursor.continue();
                };
                tx.oncomplete = () => resolve(deleted);
                tx.onerror = () => reject(tx.error ?? new Error('Failed to delete embeddings.'));
            });
        },

        async queryTopK(queryVectorArray, topK, minScore) {
            const query = queryVectorArray instanceof Float32Array ? queryVectorArray : new Float32Array(queryVectorArray || []);
            const k = Math.max(1, Number(topK) || 25);
            const threshold = Number.isFinite(minScore) ? Number(minScore) : 0.35;
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(EMBEDDINGS_STORE, 'readonly');
                const store = tx.objectStore(EMBEDDINGS_STORE);
                const req = store.openCursor();
                const hits = [];
                req.onsuccess = () => {
                    const cursor = req.result;
                    if (!cursor) {
                        hits.sort((a, b) => b.score - a.score);
                        resolve(hits.slice(0, k));
                        return;
                    }
                    const v = cursor.value;
                    if (v?.vector && v.vector.length === query.length) {
                        let dot = 0;
                        for (let i = 0; i < query.length; i++) dot += query[i] * v.vector[i];
                        if (dot >= threshold) hits.push({ key: cursor.key, score: dot });
                    }
                    cursor.continue();
                };
                req.onerror = () => reject(req.error ?? new Error('Failed to query embeddings.'));
            });
        },

        async countEmbeddings() {
            return await runTransaction(EMBEDDINGS_STORE, 'readonly', store => asPromise(store.count()));
        },

        async clearEmbeddings() {
            await runTransaction(EMBEDDINGS_STORE, 'readwrite', store => store.clear());
            return true;
        },

        async clearAll() {
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction([ATTACHMENTS_STORE, EMBEDDINGS_STORE], 'readwrite');
                tx.objectStore(ATTACHMENTS_STORE).clear();
                tx.objectStore(EMBEDDINGS_STORE).clear();
                tx.oncomplete = () => resolve(true);
                tx.onerror = () => reject(tx.error ?? new Error('Failed to clear stores.'));
            });
        },

        async estimateUsage() {
            if (navigator?.storage?.estimate) {
                try { return await navigator.storage.estimate(); } catch { return null; }
            }
            return null;
        }
    };
})();
