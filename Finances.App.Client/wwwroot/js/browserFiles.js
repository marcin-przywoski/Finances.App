window.financeBrowser = {
    downloadTextFile: function (fileName, contentType, content) {
        const blob = new Blob([content], { type: contentType || 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
    }
};

// File input helpers for Blazor components. Reads the first selected file from
// a <input type="file"> element and returns it as a base64 string with metadata.
window.browserFiles = {
    readFirstFile: async function (inputElementId) {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files || input.files.length === 0) return null;
        const file = input.files[0];
        try {
            const buffer = await file.arrayBuffer();
            const bytes = new Uint8Array(buffer);
            let binary = '';
            const chunkSize = 0x8000;
            for (let i = 0; i < bytes.length; i += chunkSize) {
                binary += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + chunkSize, bytes.length)));
            }
            const result = {
                name: file.name,
                mime: file.type || 'application/octet-stream',
                sizeBytes: file.size,
                base64: globalThis.btoa(binary)
            };
            // Reset the input so the same file can be reselected later.
            try { input.value = ''; } catch { /* ignore */ }
            return result;
        } catch (err) {
            console.error('readFirstFile failed', err);
            return null;
        }
    }
};