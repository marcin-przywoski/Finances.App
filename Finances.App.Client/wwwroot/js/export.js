// Export utilities for Finances.App
// Uses SheetJS (XLSX) for Excel and jsPDF + autoTable for PDF
// Libraries are loaded lazily on first use via CDN

window.financeExport = (function () {
    let _xlsxLoaded = false;
    let _jspdfLoaded = false;

    async function ensureXlsx() {
        if (_xlsxLoaded) return;
        await loadScript('https://cdn.sheetjs.com/xlsx-0.20.3/package/dist/xlsx.full.min.js');
        _xlsxLoaded = true;
    }

    async function ensureJsPdf() {
        if (_jspdfLoaded) return;
        await loadScript('https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.2/jspdf.umd.min.js');
        await loadScript('https://cdnjs.cloudflare.com/ajax/libs/jspdf-autotable/3.8.4/jspdf.plugin.autotable.min.js');
        _jspdfLoaded = true;
    }

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            if (document.querySelector(`script[src="${src}"]`)) { resolve(); return; }
            const s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = () => reject(new Error('Failed to load ' + src));
            document.head.appendChild(s);
        });
    }

    function triggerDownload(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        URL.revokeObjectURL(url);
    }

    return {
        /**
         * Export tabular data to XLSX
         * @param {string} fileName
         * @param {string} sheetName
         * @param {string[]} headers
         * @param {any[][]} rows  — each inner array matches headers order
         */
        exportExcel: async function (fileName, sheetName, headers, rows) {
            await ensureXlsx();
            const data = [headers, ...rows];
            const ws = XLSX.utils.aoa_to_sheet(data);

            // Auto-size columns
            ws['!cols'] = headers.map((h, i) => {
                let max = h.length;
                for (const row of rows) {
                    const val = row[i] != null ? String(row[i]) : '';
                    if (val.length > max) max = val.length;
                }
                return { wch: Math.min(max + 2, 40) };
            });

            const wb = XLSX.utils.book_new();
            XLSX.utils.book_append_sheet(wb, ws, sheetName || 'Sheet1');
            const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
            triggerDownload(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), fileName);
        },

        /**
         * Export tabular data to PDF
         * @param {string} fileName
         * @param {string} title
         * @param {string[]} headers
         * @param {any[][]} rows
         * @param {object} [options] — { subtitle, landscape }
         */
        exportPdf: async function (fileName, title, headers, rows, options) {
            await ensureJsPdf();
            const { jsPDF } = window.jspdf;
            const landscape = options && options.landscape;
            const doc = new jsPDF({ orientation: landscape ? 'landscape' : 'portrait', unit: 'mm', format: 'a4' });

            // Title
            doc.setFontSize(16);
            doc.text(title || 'Report', 14, 18);

            let startY = 24;
            if (options && options.subtitle) {
                doc.setFontSize(10);
                doc.setTextColor(100);
                doc.text(options.subtitle, 14, startY);
                startY += 6;
                doc.setTextColor(0);
            }

            doc.autoTable({
                head: [headers],
                body: rows,
                startY: startY,
                styles: { fontSize: 8, cellPadding: 2 },
                headStyles: { fillColor: [13, 148, 136], textColor: 255, fontStyle: 'bold' },
                alternateRowStyles: { fillColor: [245, 247, 250] },
                margin: { left: 14, right: 14 }
            });

            // Footer with date
            const pageCount = doc.internal.getNumberOfPages();
            for (let i = 1; i <= pageCount; i++) {
                doc.setPage(i);
                doc.setFontSize(7);
                doc.setTextColor(150);
                doc.text(
                    `Generated ${new Date().toLocaleDateString()} — Page ${i} of ${pageCount}`,
                    14,
                    doc.internal.pageSize.getHeight() - 8
                );
            }

            doc.save(fileName);
        }
    };
})();
