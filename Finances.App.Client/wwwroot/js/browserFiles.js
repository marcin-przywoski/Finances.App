window.financeBrowser = {
    downloadTextFile: function (fileName, contentType, content) {
        const blob = new Blob([content], { type: contentType || 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.style.display = 'none';
        // iOS Safari needs the anchor in the document, and revoking the
        // object URL on the same tick can cancel the download there.
        document.body.appendChild(link);
        link.click();
        setTimeout(() => {
            document.body.removeChild(link);
            URL.revokeObjectURL(url);
        }, 1000);
    },

    // Prefers the native share sheet where files can be shared (the natural
    // path in an installed mobile PWA, where a download has nowhere obvious
    // to land); falls back to a regular download. Returns
    // 'shared' | 'downloaded' | 'cancelled'.
    shareOrDownloadFile: async function (fileName, contentType, content) {
        try {
            if (navigator.canShare) {
                const file = new File([content], fileName, { type: contentType || 'text/plain' });
                if (navigator.canShare({ files: [file] })) {
                    await navigator.share({ files: [file], title: fileName });
                    return 'shared';
                }
            }
        } catch (error) {
            if (error && error.name === 'AbortError') {
                return 'cancelled';
            }
            // Any other share failure falls through to a plain download.
        }

        this.downloadTextFile(fileName, contentType, content);
        return 'downloaded';
    }
};
